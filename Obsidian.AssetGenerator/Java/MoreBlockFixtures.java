import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import it.unimi.dsi.fastutil.objects.ObjectLinkedOpenHashSet;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.Holder;
import net.minecraft.core.component.DataComponents;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.network.protocol.Packet;
import net.minecraft.network.protocol.game.ClientboundBlockEventPacket;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.dedicated.DedicatedPlayerList;
import net.minecraft.server.dedicated.DedicatedServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.server.level.ServerPlayerGameMode;
import net.minecraft.sounds.SoundEvent;
import net.minecraft.sounds.SoundSource;
import net.minecraft.util.RandomSource;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.player.Abilities;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.flag.FeatureFlagSet;
import net.minecraft.world.flag.FeatureFlags;
import net.minecraft.world.item.ItemCooldowns;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.context.DirectionalPlaceContext;
import net.minecraft.world.level.GameType;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.ScheduledTickAccess;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.NoteBlock;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.Property;
import net.minecraft.world.level.material.Fluid;
import net.minecraft.world.level.material.FluidState;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.Vec3;

/** Records construction-family state transitions and the note block's actual instrument and sound routines. */
public final class MoreBlockFixtures {
    public static void main(String[] args) throws Exception {
        var version = VanillaDumper.bootstrap();
        var root = JsonParser.parseString(Files.readString(Path.of(args[0]))).getAsJsonObject();
        root.addProperty("version", version);
        var selected = args.length > 2 ? Set.of(args[2].split("\\+")) : null;
        int captured = 0;
        for (var entry : root.getAsJsonArray("cases")) {
            var input = entry.getAsJsonObject();
            if (selected != null && !selected.contains(input.get("name").getAsString())) {
                continue;
            }

            captured++;
            var level = createLevel();
            for (var setup : input.getAsJsonArray("setup")) {
                var placement = setup.getAsJsonObject();
                level.blocks.put(position(placement.getAsJsonArray("pos")),
                    state(placement.get("state").getAsString()));
            }

            var origin = position(input.getAsJsonArray("origin"));
            var random = RandomSource.create(input.has("seed") ? input.get("seed").getAsLong() : 123);
            level.random = random;
            level.powered = input.has("powered") && input.get("powered").getAsBoolean();
            var kind = input.get("kind").getAsString();
            if (kind.equals("block_event_range")) {
                var tracker = simulationTracker(input.getAsJsonArray("sources"));
                var samples = new JsonArray();
                for (var value : input.getAsJsonArray("chunks")) {
                    var coordinates = value.getAsJsonArray();
                    var chunk = new net.minecraft.world.level.ChunkPos(
                        coordinates.get(0).getAsInt(), coordinates.get(1).getAsInt());
                    var ticketLevel = tracker.getLevel(chunk);
                    var sample = new JsonObject();
                    sample.addProperty("level", ticketLevel);
                    sample.addProperty("blockTicking",
                        net.minecraft.server.level.ChunkLevel.isBlockTicking(ticketLevel));
                    sample.addProperty("entityTicking",
                        net.minecraft.server.level.ChunkLevel.isEntityTicking(ticketLevel));
                    samples.add(sample);
                }

                input.add("samples", samples);
            } else if (kind.equals("placement")) {
                var block = state(input.get("block").getAsString()).getBlock();
                var context = new PlacementContext(level, origin,
                    direction(input.get("look").getAsString()), new ItemStack(block),
                    direction(input.get("face").getAsString()));
                var placed = block.getStateForPlacement(context);
                if (placed != null && !placed.canSurvive(level, origin)) {
                    placed = null;
                }

                input.addProperty("rotation", context.getRotation());
                input.addProperty("replacesClicked", context.replacingClickedOnBlock());
                var cursor = new JsonArray();
                cursor.add(context.getClickLocation().x - origin.getX());
                cursor.add(context.getClickLocation().y - origin.getY());
                cursor.add(context.getClickLocation().z - origin.getZ());
                input.add("cursor", cursor);
                input.addProperty("horizontal", context.getHorizontalDirection().getSerializedName());
                input.addProperty("nearest", context.getNearestLookingDirection().getSerializedName());
                input.addProperty("placed", placed == null ? null : text(placed));
            } else if (kind.equals("shape")) {
                var side = direction(input.get("direction").getAsString());
                var neighbor = origin.relative(side);
                var updated = level.getBlockState(origin).updateShape(level, level.scheduler(), origin, side,
                    neighbor, level.getBlockState(neighbor), random);
                input.addProperty("updated", text(updated));
                input.add("ticks", level.ticks);
            } else if (kind.equals("leaf_tick")) {
                level.getBlockState(origin).tick(level, origin, random);
                input.addProperty("updated", text(level.getBlockState(origin)));
                input.addProperty("nextRandom", random.nextInt());
            } else if (kind.equals("strip")) {
                var transformer = net.minecraft.world.item.Items.IRON_AXE.getDefaultInstance()
                    .get(DataComponents.BLOCK_TRANSFORMER).value();
                BlockState transformed = null;
                for (var transform : transformer.transforms()) {
                    var candidate = transform.blockStateProvider().value().getOptionalState(level, random, origin);
                    if (candidate != null) {
                        transformed = candidate;
                        input.addProperty("sound", transform.sound().value().location().toString());
                        input.addProperty("damage", transform.itemDamagePerUse());
                        break;
                    }
                }

                input.addProperty("transformed", transformed == null ? null : text(transformed));
                input.addProperty("nextRandom", random.nextInt());
            } else if (kind.equals("use")) {
                var player = (ProbePlayer)allocate(ProbePlayer.class);
                player.setYRot(input.get("rotation").getAsFloat());
                var before = level.getBlockState(origin);
                before.useWithoutItem(level, player,
                    new BlockHitResult(Vec3.atCenterOf(origin), Direction.UP, origin, false));
                input.addProperty("updated", text(level.getBlockState(origin)));
                input.add("sounds", level.sounds);
            } else if (kind.equals("placed_by")) {
                var placed = level.getBlockState(origin);
                placed.getBlock().setPlacedBy(level, origin, placed, null, new ItemStack(placed.getBlock()));
                input.addProperty("upper", text(level.getBlockState(origin.above())));
            } else if (kind.equals("block_events")) {
                initializeEvents(level);
                if (input.has("sources")) {
                    level.simulationTracker = simulationTracker(input.getAsJsonArray("sources"));
                }

                field(Level.class, "random").set(level, random);
                for (var placement : level.blocks.entrySet()) {
                    if (placement.getValue().getBlock() instanceof net.minecraft.world.level.block.EntityBlock block) {
                        level.setBlockEntity(block.newBlockEntity(placement.getKey(), placement.getValue()));
                    }
                }

                level.notifyNotes = input.has("notifyNotes") && input.get("notifyNotes").getAsBoolean();
                var phases = new JsonArray();
                for (var value : input.getAsJsonArray("actions")) {
                    var action = value.getAsJsonObject();
                    var pos = action.has("pos") ? position(action.getAsJsonArray("pos")) : origin;
                    switch (action.get("action").getAsString()) {
                        case "set" -> level.blocks.put(pos, state(action.get("state").getAsString()));
                        case "play" -> playNote(level, pos);
                        case "ticking" -> level.ticking = action.get("value").getAsBoolean();
                        case "piston" -> level.blockEvent(pos, level.getBlockState(pos).getBlock(), 0, 5);
                        case "gateway_cooldown" ->
                            net.minecraft.world.level.block.entity.TheEndGatewayBlockEntity.triggerCooldown(
                                level, pos, level.getBlockState(pos),
                                (net.minecraft.world.level.block.entity.TheEndGatewayBlockEntity)
                                    level.getBlockEntity(pos));
                        case "spawner_reset" -> {
                            var spawner = ((net.minecraft.world.level.block.entity.SpawnerBlockEntity)
                                level.getBlockEntity(pos)).getSpawner();
                            var delay = net.minecraft.world.level.BaseSpawner.class
                                .getDeclaredMethod("delay", Level.class, BlockPos.class);
                            delay.setAccessible(true);
                            delay.invoke(spawner, level, pos);
                        }
                        case "event" -> level.blockEvent(pos, level.getBlockState(pos).getBlock(),
                            action.get("id").getAsInt(), action.get("parameter").getAsInt());
                        case "phase" -> {
                            var phase = new JsonObject();
                            phase.addProperty("queued", queuedEvents(level));
                            phase.addProperty("soundsBefore", level.sounds.size());
                            phase.addProperty("eventsBefore", level.events.size());
                            runBlockEvents(level);
                            phase.addProperty("pendingAfter", queuedEvents(level));
                            phase.add("sounds", level.sounds.deepCopy());
                            phase.add("events", level.events.deepCopy());
                            var states = new JsonArray();
                            for (var watched : input.getAsJsonArray("watch")) {
                                states.add(text(level.getBlockState(position(watched.getAsJsonArray()))));
                            }

                            phase.add("states", states);
                            phases.add(phase);
                        }
                        default -> throw new IllegalArgumentException("Unknown block-event action: " + action);
                    }
                }

                input.add("phases", phases);
            } else if (kind.equals("note_lifecycle")) {
                initializeEvents(level);
                var phases = new JsonArray();
                for (var value : input.getAsJsonArray("actions")) {
                    var action = value.getAsJsonObject();
                    var pos = action.has("pos") ? position(action.getAsJsonArray("pos")) : origin;
                    switch (action.get("action").getAsString()) {
                        case "set" -> level.blocks.put(pos, state(action.get("state").getAsString()));
                        case "play" -> playNote(level, pos);
                        case "phase" -> {
                            var phase = new JsonObject();
                            phase.addProperty("queued", queuedEvents(level));
                            phase.addProperty("soundsBefore", level.sounds.size());
                            phase.addProperty("eventsBefore", level.events.size());
                            runBlockEvents(level);
                            phase.addProperty("pendingAfter", queuedEvents(level));
                            phase.add("sounds", level.sounds.deepCopy());
                            phase.add("events", level.events.deepCopy());
                            phases.add(phase);
                        }
                        default -> throw new IllegalArgumentException("Unknown lifecycle action: " + action);
                    }
                }

                input.add("phases", phases);
                input.addProperty("nextRandom", random.nextInt());
            } else if (kind.equals("use_item")) {
                initializeEvents(level);
                var player = (ProbeServerPlayer)allocate(ProbeServerPlayer.class);
                player.world = level;
                player.held = new ItemStack(BuiltInRegistries.ITEM.getValue(
                    Identifier.parse(input.get("item").getAsString())), 2);
                player.cooldowns = new ItemCooldowns();
                field(Player.class, "abilities").set(player, new Abilities());
                var advancements = allocate(net.minecraft.server.PlayerAdvancements.class);
                field(net.minecraft.server.PlayerAdvancements.class, "activeTriggers")
                    .set(advancements, new HashMap<>());
                field(ServerPlayer.class, "advancements").set(player, advancements);
                player.secondary = input.get("sneaking").getAsBoolean();
                player.setYRot(input.get("rotation").getAsFloat());
                field(ServerPlayer.class, "gameMode").set(player, new ServerPlayerGameMode(player));
                var side = direction(input.get("face").getAsString());
                var hit = new BlockHitResult(Vec3.atCenterOf(origin), side, origin, false);
                player.gameMode.useItemOn(player, level, player.held, InteractionHand.MAIN_HAND, hit);
                input.addProperty("note", level.getBlockState(origin)
                    .getValue(net.minecraft.world.level.block.state.properties.BlockStateProperties.NOTE));
                input.addProperty("target", text(level.getBlockState(origin.relative(side))));
                input.addProperty("remaining", player.held.getCount());
            } else if (kind.equals("note")) {
                var note = level.getBlockState(origin);
                var play = NoteBlock.class.getDeclaredMethod("playNote", Entity.class, BlockState.class,
                    Level.class, BlockPos.class);
                play.setAccessible(true);
                play.invoke(Blocks.NOTE_BLOCK, null, note, level, origin);
                boolean triggered = !level.events.isEmpty() && note.triggerEvent(level, origin, 0, 0);
                input.addProperty("triggered", triggered);

                input.add("events", level.events);
                input.add("sounds", level.sounds);
                input.addProperty("nextRandom", random.nextInt());
            } else {
                throw new IllegalArgumentException("Unknown probe kind: " + kind);
            }
        }

        if (selected != null && captured != selected.size()) {
            throw new IllegalArgumentException("Every selected case must exist and be named once.");
        }

        var gson = new GsonBuilder().serializeNulls().setPrettyPrinting().disableHtmlEscaping().create();
        Files.writeString(Path.of(args[0]), gson.toJson(root) + "\n");
        if (!args[1].equals("-")) {
            NoteBlockInstruments.dumpInstruments(Path.of(args[1]));
        }
    }

    private static net.minecraft.server.level.SimulationChunkTracker simulationTracker(JsonArray sources)
        throws Exception {
        var tickets = new net.minecraft.world.level.TicketStorage();
        var tracker = new net.minecraft.server.level.SimulationChunkTracker(tickets);
        for (var value : sources) {
            var source = value.getAsJsonObject();
            var center = source.getAsJsonArray("chunk");
            var chunk = new net.minecraft.world.level.ChunkPos(
                center.get(0).getAsInt(), center.get(1).getAsInt());
            var radius = source.get("radius").getAsInt();
            var type = net.minecraft.server.level.TicketType.FORCED;
            int ticketLevel;
            if (source.get("type").getAsString().equals("player")) {
                var manager = (ProbeDistanceManager)allocate(ProbeDistanceManager.class);
                field(net.minecraft.server.level.DistanceManager.class, "simulationDistance").setInt(manager, radius);
                var playerLevel = net.minecraft.server.level.DistanceManager.class
                    .getDeclaredMethod("getPlayerTicketLevel");
                playerLevel.setAccessible(true);
                ticketLevel = (int)playerLevel.invoke(manager);
                type = net.minecraft.server.level.TicketType.PLAYER_SIMULATION;
            } else if (source.get("type").getAsString().equals("loaded")) {
                type = net.minecraft.server.level.TicketType.UNKNOWN;
                ticketLevel = net.minecraft.server.level.ChunkLevel.byStatus(
                    net.minecraft.server.level.FullChunkStatus.FULL) - radius;
            } else {
                ticketLevel = net.minecraft.server.level.ChunkLevel.byStatus(
                    net.minecraft.server.level.FullChunkStatus.ENTITY_TICKING) - radius;
            }

            tickets.addTicket(new net.minecraft.server.level.Ticket(type, ticketLevel), chunk);
        }

        tracker.runAllUpdates();
        return tracker;
    }

    private static void playNote(ProbeLevel level, BlockPos pos) throws Exception {
        var play = NoteBlock.class.getDeclaredMethod("playNote", Entity.class, BlockState.class,
            Level.class, BlockPos.class);
        play.setAccessible(true);
        play.invoke(Blocks.NOTE_BLOCK, null, level.getBlockState(pos), level, pos);
    }

    private static void initializeEvents(ProbeLevel level) throws Exception {
        level.deferredEvents = true;
        field(ServerLevel.class, "blockEvents").set(level, new ObjectLinkedOpenHashSet<>());
        field(ServerLevel.class, "blockEventsToReschedule").set(level, new ArrayList<>());
        var server = (ProbeServer)allocate(ProbeServer.class);
        var players = (ProbePlayerList)allocate(ProbePlayerList.class);
        players.world = level;
        field(MinecraftServer.class, "playerList").set(server, players);
        field(ServerLevel.class, "server").set(level, server);
    }

    private static java.lang.reflect.Field field(Class<?> owner, String name) throws Exception {
        var field = owner.getDeclaredField(name);
        field.setAccessible(true);
        return field;
    }

    private static int queuedEvents(ProbeLevel level) throws Exception {
        return ((java.util.Set<?>)field(ServerLevel.class, "blockEvents").get(level)).size();
    }

    private static void runBlockEvents(ProbeLevel level) throws Exception {
        var method = ServerLevel.class.getDeclaredMethod("runBlockEvents");
        method.setAccessible(true);
        method.invoke(level);
    }

    private static ProbeLevel createLevel() throws Exception {
        var level = (ProbeLevel)allocate(ProbeLevel.class);
        level.blocks = new HashMap<>();
        level.blockEntities = new HashMap<>();
        level.ticks = new JsonArray();
        level.events = new JsonArray();
        level.sounds = new JsonArray();
        level.ticking = true;
        return level;
    }

    private static Object allocate(Class<?> type) throws Exception {
        var field = Class.forName("sun.misc.Unsafe").getDeclaredField("theUnsafe");
        field.setAccessible(true);
        var unsafe = field.get(null);
        return unsafe.getClass().getMethod("allocateInstance", Class.class).invoke(unsafe, type);
    }

    private static BlockPos position(JsonArray values) {
        return new BlockPos(values.get(0).getAsInt(), values.get(1).getAsInt(), values.get(2).getAsInt());
    }

    private static Direction direction(String name) {
        return Direction.valueOf(name.toUpperCase(java.util.Locale.ROOT));
    }

    private static String text(BlockState state) {
        return state.toString().replace("Block{", "").replace("}", "");
    }

    private static BlockState state(String text) {
        var bracket = text.indexOf('[');
        var name = bracket < 0 ? text : text.substring(0, bracket);
        var block = BuiltInRegistries.BLOCK.getValue(Identifier.parse(name));
        var state = block.defaultBlockState();
        if (bracket >= 0) {
            for (var pair : text.substring(bracket + 1, text.length() - 1).split(",")) {
                var parts = pair.split("=");
                state = property(state, block.getStateDefinition().getProperty(parts[0]), parts[1]);
            }
        }

        return state;
    }

    private static <T extends Comparable<T>> BlockState property(BlockState state, Property<T> property, String value) {
        return state.setValue(property, property.getValue(value).orElseThrow());
    }

    private static final class PlacementContext extends DirectionalPlaceContext {
        PlacementContext(ProbeLevel level, BlockPos pos, Direction look, ItemStack item, Direction face) {
            super(level, pos, look, item, face);
        }

        @Override public boolean replacingClickedOnBlock() {
            return true;
        }
    }

    private static final class ProbePlayer extends Player {
        private ProbePlayer() {
            super(null, null);
            throw new UnsupportedOperationException();
        }

        @Override public GameType gameMode() {
            return GameType.SURVIVAL;
        }
    }

    private static final class ProbeServerPlayer extends ServerPlayer {
        ProbeLevel world;
        ItemStack held;
        ItemCooldowns cooldowns;
        boolean secondary;

        private ProbeServerPlayer() {
            super(null, null, null, null);
            throw new UnsupportedOperationException();
        }

        @Override public ServerLevel level() {
            return world;
        }

        @Override public ItemStack getMainHandItem() {
            return held;
        }

        @Override public ItemStack getOffhandItem() {
            return ItemStack.EMPTY;
        }

        @Override public ItemStack getItemInHand(InteractionHand hand) {
            return hand == InteractionHand.MAIN_HAND ? held : ItemStack.EMPTY;
        }

        @Override public void setItemInHand(InteractionHand hand, ItemStack item) {
            held = item;
        }

        @Override public boolean isSecondaryUseActive() {
            return secondary;
        }

        @Override public ItemCooldowns getCooldowns() {
            return cooldowns;
        }

        @Override public void awardStat(net.minecraft.stats.Stat<?> stat, int amount) {
        }
    }

    private static final class ProbeDistanceManager extends net.minecraft.server.level.DistanceManager {
        private ProbeDistanceManager() {
            super(null, null, null);
            throw new UnsupportedOperationException();
        }

        @Override protected boolean isChunkToRemove(long chunk) {
            return false;
        }

        @Override protected net.minecraft.server.level.ChunkHolder getChunk(long chunk) {
            return null;
        }

        @Override protected net.minecraft.server.level.ChunkHolder updateChunkScheduling(long chunk, int level,
            net.minecraft.server.level.ChunkHolder holder, int oldLevel) {
            return null;
        }
    }

    private static final class ProbeServer extends DedicatedServer {
        private ProbeServer() {
            super(null, null, null, null, null, null, null, null, null, null);
            throw new UnsupportedOperationException();
        }

        @Override public net.minecraft.server.ReloadableServerRegistries.Holder reloadableRegistries() {
            return new net.minecraft.server.ReloadableServerRegistries.Holder(net.minecraft.core.RegistryAccess.EMPTY);
        }
    }

    private static final class ProbePlayerList extends DedicatedPlayerList {
        ProbeLevel world;

        private ProbePlayerList() {
            super(null, null, null);
            throw new UnsupportedOperationException();
        }

        @Override public void broadcast(Player ignored, double x, double y, double z, double range,
            ResourceKey<Level> dimension, Packet<?> packet) {
            if (packet instanceof ClientboundBlockEventPacket event) {
                var value = new JsonObject();
                value.addProperty("block", BuiltInRegistries.BLOCK.getKey(event.getBlock()).toString());
                value.addProperty("id", event.getB0());
                value.addProperty("parameter", event.getB1());
                var pos = new JsonArray();
                pos.add(event.getPos().getX());
                pos.add(event.getPos().getY());
                pos.add(event.getPos().getZ());
                value.add("pos", pos);
                world.events.add(value);
            }
        }
    }

    private static final class ProbeLevel extends ServerLevel {
        Map<BlockPos, BlockState> blocks;
        Map<BlockPos, net.minecraft.world.level.block.entity.BlockEntity> blockEntities;
        boolean notifyNotes;
        boolean ticking;
        net.minecraft.server.level.SimulationChunkTracker simulationTracker;
        JsonArray ticks;
        JsonArray events;
        JsonArray sounds;
        RandomSource random;
        boolean powered;
        boolean deferredEvents;

        private ProbeLevel() {
            super(null, null, null, null, null, null, false, 0, List.of(), false);
            throw new UnsupportedOperationException();
        }

        @Override public BlockState getBlockState(BlockPos pos) {
            return blocks.getOrDefault(pos, Blocks.AIR.defaultBlockState());
        }

        @Override public FluidState getFluidState(BlockPos pos) {
            return getBlockState(pos).getFluidState();
        }

        @Override public net.minecraft.world.level.block.entity.BlockEntity getBlockEntity(BlockPos pos) {
            return blockEntities.get(pos);
        }

        @Override public void setBlockEntity(net.minecraft.world.level.block.entity.BlockEntity entity) {
            if (!(entity instanceof net.minecraft.world.level.block.piston.PistonMovingBlockEntity)) {
                entity.setLevel(this);
            }

            blockEntities.put(entity.getBlockPos(), entity);
        }

        @Override public void removeBlockEntity(BlockPos pos) {
            blockEntities.remove(pos);
        }

        @Override public net.minecraft.world.level.border.WorldBorder getWorldBorder() {
            return new net.minecraft.world.level.border.WorldBorder();
        }

        @Override public int getSignal(BlockPos pos, Direction direction) {
            return getBlockState(pos).is(Blocks.REDSTONE_BLOCK) ? 15 : 0;
        }

        @Override public int getDirectSignal(BlockPos pos, Direction direction) {
            return getSignal(pos, direction);
        }

        @Override public void updateNeighborsAt(BlockPos pos, Block source) {
            notifyNotes(pos, source);
        }

        @Override public void updateNeighborsAt(BlockPos pos, Block source,
            net.minecraft.world.level.redstone.Orientation orientation) {
            notifyNotes(pos, source);
        }

        private void notifyNotes(BlockPos source, Block block) {
            if (!notifyNotes) {
                return;
            }

            for (var side : Direction.values()) {
                var pos = source.relative(side);
                var state = getBlockState(pos);
                if (state.is(Blocks.NOTE_BLOCK)) {
                    state.handleNeighborChanged(this, pos, block, null, false);
                }
            }
        }

        @Override public void blockEntityChanged(BlockPos pos) {
        }

        @Override public void updateNeighbourForOutputSignal(BlockPos pos, Block block) {
        }

        @Override public void sendBlockUpdated(BlockPos pos, BlockState before, BlockState after, int flags) {
        }

        @Override public long getGameTime() {
            return 0;
        }

        @Override public <T extends Entity> List<T> getEntities(
            net.minecraft.world.level.entity.EntityTypeTest<Entity, T> type,
            net.minecraft.world.phys.AABB bounds, java.util.function.Predicate<? super T> filter) {
            return List.of();
        }

        @Override public boolean shouldTickBlocksAt(long chunk) {
            return ticking && (simulationTracker == null || net.minecraft.server.level.ChunkLevel.isBlockTicking(
                simulationTracker.getLevel(net.minecraft.world.level.ChunkPos.unpack(chunk))));
        }

        @Override public FeatureFlagSet enabledFeatures() {
            return FeatureFlags.VANILLA_SET;
        }

        @Override public boolean isUnobstructed(BlockState state, BlockPos pos,
            net.minecraft.world.phys.shapes.CollisionContext context) {
            return true;
        }

        @Override public int getMinY() {
            return -64;
        }

        @Override public int getHeight() {
            return 384;
        }

        @Override public RandomSource getRandom() {
            return random;
        }

        @Override public boolean hasNeighborSignal(BlockPos pos) {
            if (powered) {
                return true;
            }

            for (var side : Direction.values()) {
                if (getSignal(pos.relative(side), side) > 0) {
                    return true;
                }
            }

            return false;
        }

        @Override public boolean setBlock(BlockPos pos, BlockState state, int flags, int limit) {
            blocks.put(pos.immutable(), state);
            if (notifyNotes && (flags & 1) != 0) {
                notifyNotes(pos, state.getBlock());
            }

            return true;
        }

        @Override public void blockEvent(BlockPos pos, Block block, int id, int parameter) {
            if (deferredEvents) {
                super.blockEvent(pos, block, id, parameter);
                return;
            }

            var value = new JsonObject();
            value.addProperty("id", id);
            value.addProperty("parameter", parameter);
            events.add(value);
        }

        @Override public void gameEvent(Holder<net.minecraft.world.level.gameevent.GameEvent> event,
            net.minecraft.world.phys.Vec3 pos, net.minecraft.world.level.gameevent.GameEvent.Context context) {
        }

        @Override public void playSound(Entity source, BlockPos pos, SoundEvent sound,
            SoundSource category, float volume, float pitch) {
            var value = new JsonObject();
            value.addProperty("sound", sound.location().toString());
            value.addProperty("volume", volume);
            value.addProperty("pitch", pitch);
            sounds.add(value);
        }

        @Override public void playSeededSound(Entity source, double x, double y, double z, Holder<SoundEvent> sound,
            SoundSource category, float volume, float pitch, long seed) {
            var value = new JsonObject();
            value.addProperty("sound", sound.value().location().toString());
            value.addProperty("volume", volume);
            value.addProperty("pitch", pitch);
            value.addProperty("seed", seed);
            if (deferredEvents) {
                var pos = new JsonArray();
                pos.add(x);
                pos.add(y);
                pos.add(z);
                value.add("pos", pos);
            }

            sounds.add(value);
        }

        ScheduledTickAccess scheduler() {
            return (ScheduledTickAccess)java.lang.reflect.Proxy.newProxyInstance(
                MoreBlockFixtures.class.getClassLoader(), new Class<?>[] { ScheduledTickAccess.class },
                (proxy, method, values) -> {
                    if (method.getName().equals("scheduleTick")) {
                        var tick = new JsonObject();
                        tick.addProperty("type", values[1] instanceof Fluid fluid
                            ? BuiltInRegistries.FLUID.getKey(fluid).toString()
                            : BuiltInRegistries.BLOCK.getKey((Block)values[1]).toString());
                        tick.addProperty("delay", (int)values[2]);
                        ticks.add(tick);
                    }

                    return null;
                });
        }
    }
}
