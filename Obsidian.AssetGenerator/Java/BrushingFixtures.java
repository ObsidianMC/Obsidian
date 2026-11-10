import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import com.mojang.authlib.GameProfile;
import com.mojang.serialization.JsonOps;
import io.netty.channel.ChannelFutureListener;
import java.lang.reflect.Field;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.Holder;
import net.minecraft.core.SectionPos;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.NbtOps;
import net.minecraft.network.protocol.Packet;
import net.minecraft.network.protocol.game.ClientboundEntityEventPacket;
import net.minecraft.network.protocol.game.ClientboundLevelEventPacket;
import net.minecraft.network.protocol.game.ClientboundLevelParticlesPacket;
import net.minecraft.network.protocol.game.ClientboundSoundPacket;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ChunkMap;
import net.minecraft.server.level.ClientInformation;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.server.network.ServerGamePacketListenerImpl;
import net.minecraft.server.network.ServerPlayerConnection;
import net.minecraft.server.players.PlayerList;
import net.minecraft.stats.Stat;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.InteractionResult;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.item.FallingBlockEntity;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.GameType;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.entity.BrushableBlockEntity;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.Property;
import net.minecraft.world.level.gameevent.BlockPositionSource;
import net.minecraft.world.level.gameevent.GameEvent;
import net.minecraft.world.level.gameevent.GameEventListener;
import net.minecraft.world.level.gameevent.PositionSource;
import net.minecraft.world.level.redstone.CollectingNeighborUpdater;
import net.minecraft.world.level.redstone.Orientation;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.Vec3;

/**
 * Records a player brushing suspicious sand and gravel (vanilla's {@code BrushItem}, {@code BrushableBlock} and
 * {@code BrushableBlockEntity}), on a headless dedicated server in a flat world of air.
 *
 * <p>Each case of the fixture is its input: the blocks around the target, the target's loot table and seed or item,
 * where the user stands and looks, the item in hand, the game mode, how many ticks to run and the actions taken
 * between ticks (looking or moving elsewhere, releasing the use, replacing a block, choosing another hotbar slot,
 * clicking again). Tick 0 is the click, through {@code ServerPlayerGameMode.useItemOn}. Each later tick runs as a
 * server tick does for this area: the game time advances, due block ticks run, the entities in the area tick, then
 * the user's use of the item ticks ({@code LivingEntity.updatingUsingItem}).
 *
 * <p>For each tick that did something the probe records the ordered block writes with their flags, the sounds,
 * level events and particles a second player standing by received, the entity events the user's tracked entity
 * broadcast, the game events, the statistics awarded, the advancement criteria completed, the item in the used hand,
 * whether the user is still using it, the target block entity's saved data when it changed and the data its client
 * update carries when the target's state changed, and the entities that appeared or went.
 *
 * <p>Writes are recorded through a neighbour updater that wraps the level's own: a write with
 * {@code UPDATE_NEIGHBORS} updates its neighbours, and one without {@code UPDATE_KNOWN_SHAPE} updates its neighbours'
 * shapes with its flags less {@code UPDATE_NEIGHBORS} and {@code UPDATE_SUPPRESS_DROPS}, so together they give the
 * write's flags. A write that does neither is found by comparing the area after the tick and has null flags.
 */
public final class BrushingFixtures {
    // How far from the target the area that is cleared before and compared after each tick reaches.
    private static final int Reach = 4;

    // Set without neighbor or shape updates and without drops, like the replay's setup.
    private static final int QuietFlags =
        Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE | Block.UPDATE_SUPPRESS_DROPS | Block.UPDATE_SKIP_ON_PLACE;

    private static MinecraftServer server;

    // Positions in what is recorded are relative to the target.
    private static BlockPos origin;

    public static void main(String[] args) throws Exception {
        var fixturePath = Path.of(args[0]);
        var root = JsonParser.parseString(Files.readString(fixturePath)).getAsJsonObject();

        try {
            startServer();
            forceArea(root);
            server.submit(() -> {
                try {
                    var selected = args.length > 1 && !args[1].isBlank()
                        ? Set.of(args[1].split("\\+")) : Set.<String>of();
                    capture(root, selected);
                } catch (Exception e) {
                    throw new RuntimeException(e);
                }
            }).join();
        } catch (Throwable e) {
            e.printStackTrace();
            System.out.flush();
            Runtime.getRuntime().halt(1);
        }

        root.addProperty("version", server.getServerVersion());
        var gson = new GsonBuilder().setPrettyPrinting().disableHtmlEscaping().serializeNulls().create();
        Files.writeString(fixturePath, compact(gson.toJson(root)) + "\n", StandardCharsets.UTF_8);
        System.out.println("Wrote " + fixturePath);
        System.out.flush();

        // The world is thrown away, so skip the server's save on exit.
        Runtime.getRuntime().halt(0);
    }

    // Forces the chunks around the target and waits until they tick, so entities can be found and ticked.
    private static void forceArea(JsonObject root) throws Exception {
        var target = position(root.getAsJsonArray("target"));
        var level = server.overworld();
        server.submit(() -> {
            for (int x = (target.getX() - Reach - 1) >> 4; x <= (target.getX() + Reach + 1) >> 4; x++) {
                for (int z = (target.getZ() - Reach - 1) >> 4; z <= (target.getZ() + Reach + 1) >> 4; z++)
                    level.setChunkForced(x, z, true);
            }
        }).join();

        var chunk = net.minecraft.world.level.ChunkPos.containing(target).pack();
        while (!server.submit(() -> level.shouldTickBlocksAt(chunk) && level.areEntitiesLoaded(chunk)).join())
            Thread.sleep(50);
    }

    private static void capture(JsonObject root, Set<String> selected) throws Exception {
        var level = server.overworld();
        var target = position(root.getAsJsonArray("target"));
        origin = target;

        var user = new ProbePlayer(server, level, profile("User"));
        var userPackets = new ArrayList<Packet<?>>();
        user.connection = recorder(userPackets);
        user.connection.player = user;
        players(server.getPlayerList()).add(user);

        // A second player above the target hears what is broadcast around it and sees the user's entity.
        var bystander = new ProbePlayer(server, level, profile("Bystander"));
        var heard = new ArrayList<Packet<?>>();
        bystander.connection = recorder(heard);
        bystander.connection.player = bystander;
        bystander.snapTo(target.getX() + 0.5, target.getY() + 3, target.getZ() + 0.5, 0, 0);
        players(server.getPlayerList()).add(bystander);
        track(level, user, bystander);

        var writes = new ArrayList<JsonObject>();
        installRecorder(level, writes);

        var gameEvents = new ArrayList<String>();
        var listener = new Listener(target, user, gameEvents);
        level.getChunkAt(target).getListenerRegistry(SectionPos.blockToSectionCoord(target.getY())).register(listener);

        for (var element : root.getAsJsonArray("cases")) {
            var input = element.getAsJsonObject();
            if (!selected.isEmpty() && !selected.contains(input.get("name").getAsString()))
                continue;

            runCase(level, root, input, user, bystander, userPackets, heard, writes, gameEvents);
        }

        clear(level, target);
        discardEntities(level, target);
        players(server.getPlayerList()).remove(user);
        players(server.getPlayerList()).remove(bystander);
        System.out.println("Brushed " + root.getAsJsonArray("cases").size() + " cases");
    }

    private static void runCase(ServerLevel level, JsonObject root, JsonObject input, ProbePlayer user,
        ProbePlayer bystander, List<Packet<?>> userPackets, List<Packet<?>> heard, List<JsonObject> writes,
        List<String> gameEvents) throws Exception {
        var target = origin;

        // A fresh area, user and hand for the case.
        user.stopUsingItem();
        clear(level, target);
        discardEntities(level, target);
        setup(level, input);
        prepare(user, root, input);
        var advancement = prepareCriteria(level, user, input);

        var timeline = new JsonArray();
        var known = new java.util.HashMap<Integer, String>();
        for (var entity : entities(level, target))
            known.put(entity.getId(), BuiltInRegistries.ENTITY_TYPE.getKey(entity.getType()).toString());

        var savedData = blockEntityData(level, target);
        var ticks = input.get("ticks").getAsInt();
        for (int tick = 0; tick <= ticks; tick++) {
            userPackets.clear();
            heard.clear();
            writes.clear();
            gameEvents.clear();
            user.awarded.clear();
            var before = snapshot(level, target);
            var doneBefore = criteriaDone(user, advancement);

            String result = null;
            for (var action : actions(input, tick))
                result = act(level, user, action, result);

            if (tick == 0) {
                result = result(click(level, user, input));
            } else {
                runTick(level, user);
            }

            var record = new JsonObject();
            record.addProperty("tick", tick);
            if (result != null)
                record.addProperty("result", result);

            var recordedWrites = recordWrites(level, target, before, writes);
            record.add("writes", recordedWrites);
            recordPackets(record, heard, userPackets, user);
            record.add("gameEvents", strings(gameEvents));
            record.add("stats", strings(user.awarded));
            record.add("criteria", newCriteria(user, advancement, doneBefore));
            record.addProperty("hand", stack(user.getItemInHand(hand(input))));
            record.addProperty("using", user.isUsingItem());

            var data = blockEntityData(level, target);
            if (!java.util.Objects.equals(data, savedData))
                record.add("blockEntity", data == null ? com.google.gson.JsonNull.INSTANCE : data);
            savedData = data;

            if (wroteTarget(recordedWrites) && level.getBlockEntity(target) instanceof BrushableBlockEntity brushable)
                record.add("clientData", json(brushable.getUpdateTag(level.registryAccess())));

            record.add("entities", entityChanges(level, target, known));

            if (meaningful(record))
                timeline.add(record);
        }

        user.stopUsingItem();
        input.add("timeline", timeline);
    }

    // A tick's record is kept when anything besides the hand and the use flag happened, or either changed.
    private static String lastHand;
    private static boolean lastUsing;

    private static boolean meaningful(JsonObject record) {
        var hand = record.get("hand").getAsString();
        var using = record.get("using").getAsBoolean();
        var changed = !hand.equals(lastHand) || using != lastUsing || record.get("tick").getAsInt() == 0;
        lastHand = hand;
        lastUsing = using;
        if (changed || record.has("result") || record.has("blockEntity") || record.has("clientData"))
            return true;

        for (var name : new String[] { "writes", "sounds", "levelEvents", "particles", "entityEvents",
            "gameEvents", "stats", "criteria", "entities" }) {
            if (!record.getAsJsonArray(name).isEmpty())
                return true;
        }

        return false;
    }

    // One server tick as it reaches this area: the game time, the due block ticks, the area's entities, then the
    // user's use of the item, which a player's own tick runs after the levels have ticked.
    private static void runTick(ServerLevel level, ProbePlayer user) throws Exception {
        var time = level.getGameTime() + 1;
        var data = field(ServerLevel.class, "serverLevelData");
        ((net.minecraft.world.level.storage.ServerLevelData) data.get(level)).setGameTime(time);

        level.getBlockTicks().tick(time, 65536, (pos, block) -> {
            var state = level.getBlockState(pos);
            if (state.is(block))
                state.tick(level, pos, level.getRandom());
        });

        for (var entity : entities(level, origin)) {
            if (!entity.isRemoved())
                level.tickNonPassenger(entity);
        }

        var use = LivingEntity.class.getDeclaredMethod("updatingUsingItem");
        use.setAccessible(true);
        use.invoke(user);
    }

    // The click, as the server runs a use-item-on packet: the clicked face of the target at the case's point.
    private static InteractionResult click(ServerLevel level, ProbePlayer user, JsonObject input) {
        var clicked = origin.offset(input.has("clicked") ? position(input.getAsJsonArray("clicked")) : BlockPos.ZERO);
        var face = Direction.valueOf(input.get("face").getAsString().toUpperCase(Locale.ROOT));
        var location = Vec3.atCenterOf(clicked).relative(face, 0.5);
        var hit = new BlockHitResult(location, face, clicked, false);
        var hand = hand(input);
        return user.gameMode.useItemOn(user, level, user.getItemInHand(hand), hand, hit);
    }

    private static List<JsonObject> actions(JsonObject input, int tick) {
        var found = new ArrayList<JsonObject>();
        if (!input.has("actions"))
            return found;

        for (var element : input.getAsJsonArray("actions")) {
            var action = element.getAsJsonObject();
            if (action.get("tick").getAsInt() == tick)
                found.add(action);
        }

        return found;
    }

    // An action between ticks, as a packet or a command would take it.
    private static String act(ServerLevel level, ProbePlayer user, JsonObject action, String result) {
        var kind = action.get("kind").getAsString();
        switch (kind) {
            case "look" -> {
                var rotation = action.getAsJsonArray("rotation");
                user.setYRot(rotation.get(0).getAsFloat());
                user.setXRot(rotation.get(1).getAsFloat());
                face(user);
            }
            case "move" -> {
                var to = action.getAsJsonArray("position");
                user.snapTo(to.get(0).getAsDouble(), to.get(1).getAsDouble(), to.get(2).getAsDouble(),
                    user.getYRot(), user.getXRot());
                user.setOldPosAndRot();
            }
            case "release" -> user.releaseUsingItem();
            case "set" -> {
                var pos = origin.offset(position(action.getAsJsonArray("offset")));
                level.setBlock(pos, state(action.get("state").getAsString()), Block.UPDATE_ALL);
            }
            case "select" -> user.getInventory().setSelectedSlot(action.get("slot").getAsInt());
            case "click" -> {
                var face = Direction.valueOf(action.get("face").getAsString().toUpperCase(Locale.ROOT));
                var location = Vec3.atCenterOf(origin).relative(face, 0.5);
                var hit = new BlockHitResult(location, face, origin, false);
                var hand = user.getUsedItemHand();
                return result(user.gameMode.useItemOn(user, level, user.getItemInHand(hand), hand, hit));
            }
            default -> throw new IllegalArgumentException("Unknown action " + kind);
        }

        return result;
    }

    private static void setup(ServerLevel level, JsonObject input) {
        for (var element : input.getAsJsonArray("setup")) {
            var placement = element.getAsJsonObject();
            var pos = origin.offset(position(placement.getAsJsonArray("offset")));
            level.setBlock(pos, state(placement.get("state").getAsString()), QuietFlags);
        }

        if (!(level.getBlockEntity(origin) instanceof BrushableBlockEntity brushable))
            return;

        if (input.has("lootTable")) {
            var key = ResourceKey.create(Registries.LOOT_TABLE, Identifier.parse(input.get("lootTable").getAsString()));
            brushable.setLootTable(key, input.has("seed") ? input.get("seed").getAsLong() : 0L);
        }

        if (input.has("storedItem")) {
            var item = BuiltInRegistries.ITEM.getValue(Identifier.parse(input.get("storedItem").getAsString()));
            var tag = new CompoundTag();
            var stack = new CompoundTag();
            stack.putString("id", BuiltInRegistries.ITEM.getKey(item).toString());
            stack.putInt("count", 1);
            tag.put("item", stack);
            loadBlockEntity(level, brushable, tag);
        }
    }

    private static void loadBlockEntity(ServerLevel level, BrushableBlockEntity brushable, CompoundTag tag) {
        var reporter = net.minecraft.util.ProblemReporter.DISCARDING;
        var input = net.minecraft.world.level.storage.TagValueInput.create(reporter, level.registryAccess(), tag);
        brushable.loadWithComponents(input);
    }

    // The user stands and looks where the case (or the fixture) says, in the case's game mode, holding its item.
    private static void prepare(ProbePlayer user, JsonObject root, JsonObject input) {
        var standing = input.has("player") ? input.getAsJsonArray("player") : root.getAsJsonArray("player");
        var mode = input.has("gameMode") ? input.get("gameMode").getAsString() : "survival";
        user.setGameMode(GameType.byName(mode));
        user.snapTo(
            standing.get(0).getAsDouble(),
            standing.get(1).getAsDouble(),
            standing.get(2).getAsDouble(),
            standing.get(3).getAsFloat(),
            standing.get(4).getAsFloat());
        face(user);
        user.setShiftKeyDown(false);
        user.getInventory().clearContent();
        user.getInventory().setSelectedSlot(0);

        var item = BuiltInRegistries.ITEM.getValue(Identifier.parse(
            input.has("item") ? input.get("item").getAsString() : "minecraft:brush"));
        var stack = new ItemStack(item, input.has("count") ? input.get("count").getAsInt() : 1);
        if (input.has("damage"))
            stack.setDamageValue(input.get("damage").getAsInt());

        user.setItemInHand(hand(input), stack);
        lastHand = null;
        lastUsing = false;
    }

    // A living entity aims with its head, as of the last tick: the user's head and last rotations are its rotation.
    private static void face(ProbePlayer user) {
        user.setYHeadRot(user.getYRot());
        user.setOldPosAndRot();
        user.yHeadRotO = user.yHeadRot;
    }

    private static InteractionHand hand(JsonObject input) {
        return input.has("handUsed") && input.get("handUsed").getAsString().equals("offhand")
            ? InteractionHand.OFF_HAND : InteractionHand.MAIN_HAND;
    }

    // Registers the case's advancement criteria with the user, as BlockUseFixtures does.
    private static net.minecraft.advancements.AdvancementHolder prepareCriteria(
        ServerLevel level, ProbePlayer user, JsonObject input) throws Exception {
        if (!input.has("criteria"))
            return null;

        var definition = new JsonObject();
        definition.add("criteria", input.getAsJsonObject("criteria").deepCopy());
        var ops = server.reloadableRegistries().lookup().createSerializationContext(JsonOps.INSTANCE);
        var value = net.minecraft.advancements.Advancement.CODEC.parse(ops, definition).getOrThrow();
        var id = Identifier.parse("probe:" + input.get("name").getAsString());
        var holder = new net.minecraft.advancements.AdvancementHolder(id, value);
        user.getAdvancements().getOrStartProgress(holder);
        var register = net.minecraft.server.PlayerAdvancements.class.getDeclaredMethod(
            "registerListeners", net.minecraft.advancements.AdvancementHolder.class);
        register.setAccessible(true);
        register.invoke(user.getAdvancements(), holder);
        return holder;
    }

    private static Set<String> criteriaDone(ProbePlayer user, net.minecraft.advancements.AdvancementHolder holder) {
        var done = new HashSet<String>();
        if (holder == null)
            return done;

        var progress = user.getAdvancements().getOrStartProgress(holder);
        for (var name : holder.value().criteria().keySet()) {
            if (progress.getCriterion(name).isDone())
                done.add(name);
        }

        return done;
    }

    private static JsonArray newCriteria(ProbePlayer user, net.minecraft.advancements.AdvancementHolder holder,
        Set<String> before) {
        var completed = new ArrayList<String>();
        for (var name : criteriaDone(user, holder)) {
            if (!before.contains(name))
                completed.add(name);
        }

        completed.sort(String::compareTo);
        return strings(completed);
    }

    // The writes the recording neighbour updater saw, then any change it didn't see, with null flags.
    private static JsonArray recordWrites(ServerLevel level, BlockPos target, List<BlockState> before,
        List<JsonObject> writes) {
        var recorded = new JsonArray();
        var seen = new HashSet<BlockPos>();
        for (var write : writes) {
            var copy = write.deepCopy();
            copy.remove("shaped");
            recorded.add(copy);
            seen.add(position(write.getAsJsonArray("pos")).offset(target));
        }

        int index = 0;
        for (var pos : compared(target)) {
            var state = level.getBlockState(pos);
            if (state != before.get(index) && !seen.contains(pos)) {
                var write = new JsonObject();
                write.add("pos", offset(pos));
                write.addProperty("state", text(state));
                write.add("flags", com.google.gson.JsonNull.INSTANCE);
                recorded.add(write);
            }

            index++;
        }

        return recorded;
    }

    private static boolean wroteTarget(JsonArray writes) {
        for (var write : writes) {
            var pos = write.getAsJsonObject().getAsJsonArray("pos");
            if (pos.get(0).getAsInt() == 0 && pos.get(1).getAsInt() == 0 && pos.get(2).getAsInt() == 0)
                return true;
        }

        return false;
    }

    // The sounds, level events and particles the bystander received, and the entity events the user's entity sent.
    private static void recordPackets(JsonObject record, List<Packet<?>> heard, List<Packet<?>> userPackets,
        ProbePlayer user) {
        var sounds = new ArrayList<String>();
        var levelEvents = new ArrayList<String>();
        var particles = new ArrayList<String>();
        var entityEvents = new ArrayList<String>();
        for (var packet : heard) {
            if (packet instanceof ClientboundSoundPacket sound) {
                sounds.add(sound.getSound().value().location() + " " + sound.getSource().getName() + " "
                    + relative(sound.getX(), sound.getY(), sound.getZ()) + " " + sound.getVolume() + " "
                    + sound.getPitch());
            } else if (packet instanceof ClientboundLevelEventPacket event) {
                levelEvents.add(event.getType() + " " + text(offset(event.getPos())) + " " + event.getData());
            } else if (packet instanceof ClientboundLevelParticlesPacket particle) {
                var type = BuiltInRegistries.PARTICLE_TYPE.getKey(particle.particle().getType());
                particles.add(type + " " + relative(particle.x(), particle.y(), particle.z()) + " "
                    + particle.count());
            } else if (packet instanceof ClientboundEntityEventPacket event) {
                entityEvents.add("bystander " + event.getEventId());
            }
        }

        for (var packet : userPackets) {
            if (packet instanceof ClientboundEntityEventPacket event)
                entityEvents.add("user " + event.getEventId());
        }

        record.add("sounds", strings(sounds));
        record.add("levelEvents", strings(levelEvents));
        record.add("particles", strings(particles));
        record.add("entityEvents", strings(entityEvents));
    }

    // Item entities as "item <id> x<count> at x,y,z motion x,y,z", falling blocks by their state, and the entities
    // that went as "removed <type>".
    private static JsonArray entityChanges(ServerLevel level, BlockPos target, Map<Integer, String> known) {
        var changes = new ArrayList<String>();
        var present = new HashSet<Integer>();
        for (var entity : entities(level, target)) {
            present.add(entity.getId());
            if (!known.containsKey(entity.getId())) {
                known.put(entity.getId(), BuiltInRegistries.ENTITY_TYPE.getKey(entity.getType()).toString());
                changes.add(describe(entity));
            }
        }

        for (var id : new ArrayList<>(known.keySet())) {
            if (!present.contains(id))
                changes.add("removed " + known.remove(id));
        }

        return strings(changes);
    }

    private static String describe(Entity entity) {
        var at = " at " + relative(entity.getX(), entity.getY(), entity.getZ());
        var motion = entity.getDeltaMovement();
        var moving = " motion " + motion.x + "," + motion.y + "," + motion.z;
        if (entity instanceof ItemEntity item)
            return "item " + stack(item.getItem()) + at + moving;

        if (entity instanceof FallingBlockEntity falling)
            return "falling_block " + text(falling.getBlockState()) + at + moving;

        return BuiltInRegistries.ENTITY_TYPE.getKey(entity.getType()) + at;
    }

    private static List<Entity> entities(ServerLevel level, BlockPos target) {
        return level.getEntities((Entity) null, new AABB(target).inflate(Reach + 1),
            entity -> !(entity instanceof ServerPlayer));
    }

    private static void discardEntities(ServerLevel level, BlockPos target) {
        for (var entity : entities(level, target))
            entity.discard();
    }

    // The target's saved data as JSON, without its id and position, or null without a block entity.
    private static JsonObject blockEntityData(ServerLevel level, BlockPos target) {
        var blockEntity = level.getBlockEntity(target);
        if (blockEntity == null)
            return null;

        return json(blockEntity.saveWithoutMetadata(level.registryAccess()));
    }

    private static JsonObject json(CompoundTag tag) {
        return NbtOps.INSTANCE.convertTo(JsonOps.INSTANCE, tag).getAsJsonObject();
    }

    // Replaces the level's neighbour updater with one that records each write and its flags.
    private static void installRecorder(ServerLevel level, List<JsonObject> writes) throws Exception {
        var field = field(Level.class, "neighborUpdater");
        field.set(level, new RecordingUpdater(level, server.getMaxChainedNeighborUpdates(), writes));
    }

    // Tracks the user's entity, with the bystander seeing it, so its entity events and equipment reach them.
    private static void track(ServerLevel level, ProbePlayer user, ProbePlayer bystander) throws Exception {
        var chunkMap = level.getChunkSource().chunkMap;
        var trackedClass = Class.forName("net.minecraft.server.level.ChunkMap$TrackedEntity");
        var constructor = trackedClass.getDeclaredConstructors()[0];
        constructor.setAccessible(true);
        var interval = constructor.getParameterTypes()[3];
        var never = interval.getField("NEVER").get(null);
        var tracked = constructor.newInstance(chunkMap, user, 160, never, true);

        var seenBy = trackedClass.getDeclaredField("seenBy");
        seenBy.setAccessible(true);
        @SuppressWarnings("unchecked")
        var connections = (Set<ServerPlayerConnection>) seenBy.get(tracked);
        connections.add(bystander.connection);

        var entityMap = field(ChunkMap.class, "entityMap");
        @SuppressWarnings("unchecked")
        var map = (it.unimi.dsi.fastutil.ints.Int2ObjectMap<Object>) entityMap.get(chunkMap);
        map.put(user.getId(), tracked);
    }

    private static Field field(Class<?> owner, String name) throws Exception {
        var field = owner.getDeclaredField(name);
        field.setAccessible(true);
        return field;
    }

    private static GameProfile profile(String name) {
        return new GameProfile(UUID.nameUUIDFromBytes(name.getBytes(StandardCharsets.UTF_8)), name);
    }

    private static String result(InteractionResult result) {
        if (result instanceof InteractionResult.Success success) {
            return switch (success.swingSource()) {
                case PREDICTED -> "success";
                case SERVER_ONLY -> "success_server";
                case NONE -> "consume";
            };
        }

        if (result instanceof InteractionResult.Fail)
            return "fail";

        if (result instanceof InteractionResult.TryEmptyHandInteraction)
            return "try_with_empty_hand";

        return "pass";
    }

    // An item stack as "<id> x<count>", with its damage when it has any, or "empty".
    private static String stack(ItemStack stack) {
        if (stack.isEmpty())
            return "empty";

        var text = BuiltInRegistries.ITEM.getKey(stack.getItem()) + " x" + stack.getCount();
        return stack.getDamageValue() > 0 ? text + " damage=" + stack.getDamageValue() : text;
    }

    @SuppressWarnings("unchecked")
    private static List<ServerPlayer> players(PlayerList list) throws Exception {
        var field = PlayerList.class.getDeclaredField("players");
        field.setAccessible(true);
        return (List<ServerPlayer>) field.get(list);
    }

    // A game connection that keeps the packets sent to it instead of sending them.
    private static ServerGamePacketListenerImpl recorder(List<Packet<?>> packets) throws Exception {
        var unsafeField = Class.forName("sun.misc.Unsafe").getDeclaredField("theUnsafe");
        unsafeField.setAccessible(true);
        var unsafe = unsafeField.get(null);
        var allocate = unsafe.getClass().getMethod("allocateInstance", Class.class);
        var recorder = (Recorder) allocate.invoke(unsafe, Recorder.class);
        recorder.packets = packets;
        return recorder;
    }

    private static void clear(ServerLevel level, BlockPos target) {
        var air = Blocks.AIR.defaultBlockState();
        for (var pos : BlockPos.betweenClosed(target.offset(-Reach - 1, -Reach - 1, -Reach - 1),
            target.offset(Reach + 1, Reach + 1, Reach + 1))) {
            level.setBlock(pos, air, QuietFlags);
        }

        // Ticks a case scheduled must not reach the next one.
        level.getBlockTicks().clearArea(new net.minecraft.world.level.levelgen.structure.BoundingBox(
            target.getX() - Reach - 1, target.getY() - Reach - 1, target.getZ() - Reach - 1,
            target.getX() + Reach + 1, target.getY() + Reach + 1, target.getZ() + Reach + 1));
    }

    private static List<BlockState> snapshot(ServerLevel level, BlockPos target) {
        var states = new ArrayList<BlockState>();
        for (var pos : compared(target))
            states.add(level.getBlockState(pos));

        return states;
    }

    // The compared area in a fixed order: y, then z, then x ascending.
    private static List<BlockPos> compared(BlockPos target) {
        var positions = new ArrayList<BlockPos>();
        for (int y = -Reach; y <= Reach; y++) {
            for (int z = -Reach; z <= Reach; z++) {
                for (int x = -Reach; x <= Reach; x++)
                    positions.add(target.offset(x, y, z));
            }
        }

        return positions;
    }

    // The state's block and the properties it doesn't share with the block's default state.
    private static String text(BlockState state) {
        var name = BuiltInRegistries.BLOCK.getKey(state.getBlock()).toString();
        var defaults = state.getBlock().defaultBlockState();
        var properties = new ArrayList<String>();
        for (Property<?> property : state.getProperties()) {
            if (!state.getValue(property).equals(defaults.getValue(property)))
                properties.add(property.getName() + "=" + value(state, property));
        }

        return properties.isEmpty() ? name : name + "[" + String.join(",", properties) + "]";
    }

    private static <T extends Comparable<T>> String value(BlockState state, Property<T> property) {
        return property.getName(state.getValue(property));
    }

    private static BlockState state(String text) {
        var bracket = text.indexOf('[');
        var name = bracket < 0 ? text : text.substring(0, bracket);
        var block = BuiltInRegistries.BLOCK.getValue(Identifier.parse(name));
        var state = block.defaultBlockState();
        if (bracket >= 0) {
            for (var pair : text.substring(bracket + 1, text.length() - 1).split(",")) {
                var parts = pair.split("=");
                state = withValue(state, block.getStateDefinition().getProperty(parts[0]), parts[1]);
            }
        }

        return state;
    }

    private static <T extends Comparable<T>> BlockState withValue(BlockState state, Property<T> property,
        String value) {
        return state.setValue(property, property.getValue(value).orElseThrow());
    }

    private static BlockPos position(JsonArray values) {
        return new BlockPos(values.get(0).getAsInt(), values.get(1).getAsInt(), values.get(2).getAsInt());
    }

    private static JsonArray offset(BlockPos pos) {
        var relative = pos.subtract(origin);
        var array = new JsonArray();
        array.add(relative.getX());
        array.add(relative.getY());
        array.add(relative.getZ());
        return array;
    }

    private static String text(JsonArray offset) {
        return offset.get(0).getAsInt() + "," + offset.get(1).getAsInt() + "," + offset.get(2).getAsInt();
    }

    private static String relative(double x, double y, double z) {
        return (x - origin.getX()) + "," + (y - origin.getY()) + "," + (z - origin.getZ());
    }

    private static JsonArray strings(List<String> values) {
        var array = new JsonArray();
        for (var value : values)
            array.add(value);

        return array;
    }

    // Keeps each short array of numbers on one line.
    private static String compact(String json) {
        var numbers = java.util.regex.Pattern.compile("\\[\\s*(-?[\\d.]+(?:,\\s*-?[\\d.]+)*)\\s*\\]");
        var matcher = numbers.matcher(json.replace("\r\n", "\n"));
        var result = new StringBuilder();
        while (matcher.find()) {
            var inner = matcher.group(1).replaceAll("\\s+", "").replace(",", ", ");
            matcher.appendReplacement(result, java.util.regex.Matcher.quoteReplacement("[" + inner + "]"));
        }

        matcher.appendTail(result);
        return result.toString();
    }

    private static void startServer() throws Exception {
        Files.writeString(Path.of("eula.txt"), "eula=true\n");

        // A flat world of air in plains, with nothing spawning and no watchdog, since the whole capture runs as
        // one task on the server thread.
        var properties = String.join("\n",
            "level-type=minecraft\\:flat",
            "generator-settings={\"layers\"\\:[{\"block\"\\:\"minecraft\\:air\",\"height\"\\:1}],"
                + "\"biome\"\\:\"minecraft\\:plains\"}",
            "generate-structures=false",
            "spawn-protection=0",
            "gamemode=survival",
            "online-mode=false",
            "server-port=0",
            "max-tick-time=-1",
            "pause-when-empty-seconds=-1",
            "sync-chunk-writes=false",
            "view-distance=2",
            "simulation-distance=2",
            "enable-jmx-monitoring=false",
            "enable-rcon=false",
            "enable-query=false",
            "");
        Files.writeString(Path.of("server.properties"), properties);

        net.minecraft.server.Main.main(new String[]{"--nogui"});

        // Main keeps the server only in its shutdown hook, so find it there.
        var hooksField = Class.forName("java.lang.ApplicationShutdownHooks").getDeclaredField("hooks");
        hooksField.setAccessible(true);
        @SuppressWarnings("unchecked")
        var hooks = (Map<Thread, Thread>) hooksField.get(null);

        for (var hook : new ArrayList<>(hooks.keySet())) {
            if (hook.getClass().getName().equals("net.minecraft.server.Main$1")) {
                var field = hook.getClass().getDeclaredField("val$dedicatedServer");
                field.setAccessible(true);
                server = (MinecraftServer) field.get(hook);
            }
        }

        if (server == null)
            throw new IllegalStateException("The dedicated server didn't start.");

        while (!server.isReady())
            Thread.sleep(50);
    }

    /**
     * The level's neighbour updater, recording each write it hears of: a write's neighbour update gives it
     * {@code UPDATE_NEIGHBORS}, and its shape updates give the rest of its flags.
     */
    private static final class RecordingUpdater extends CollectingNeighborUpdater {
        private final Level level;
        private final List<JsonObject> writes;

        RecordingUpdater(Level level, int limit, List<JsonObject> writes) {
            super(level, limit);
            this.level = level;
            this.writes = writes;
        }

        @Override public void updateNeighborsAtExceptFromFacing(BlockPos pos, Block block, Direction skip,
            Orientation orientation) {
            var write = new JsonObject();
            write.add("pos", offset(pos));
            write.addProperty("state", text(level.getBlockState(pos)));
            write.addProperty("flags", Block.UPDATE_NEIGHBORS);
            write.addProperty("shaped", false);
            writes.add(write);
            super.updateNeighborsAtExceptFromFacing(pos, block, skip, orientation);
        }

        @Override public void shapeUpdate(Direction direction, BlockState neighborState, BlockPos pos,
            BlockPos neighborPos, int flags, int limit) {
            var written = offset(neighborPos);
            JsonObject last = writes.isEmpty() ? null : writes.get(writes.size() - 1);
            var same = last != null
                && last.getAsJsonArray("pos").equals(written)
                && !last.get("shaped").getAsBoolean()
                && last.get("state").getAsString().equals(text(level.getBlockState(neighborPos)));
            if (same) {
                last.addProperty("flags", last.get("flags").getAsInt() | flags);
                last.addProperty("shaped", true);
            } else if (last == null || !last.getAsJsonArray("pos").equals(written)
                || !last.get("shaped").getAsBoolean()) {
                var write = new JsonObject();
                write.add("pos", written);
                write.addProperty("state", text(level.getBlockState(neighborPos)));
                write.addProperty("flags", flags);
                write.addProperty("shaped", true);
                writes.add(write);
            }

            super.shapeUpdate(direction, neighborState, pos, neighborPos, flags, limit);
        }
    }

    /** A player whose statistics are recorded. */
    private static final class ProbePlayer extends ServerPlayer {
        final List<String> awarded = new ArrayList<>();

        ProbePlayer(MinecraftServer server, ServerLevel level, GameProfile profile) {
            super(server, level, profile, ClientInformation.createDefault());
        }

        @Override public void awardStat(Stat<?> stat, int amount) {
            awarded.add(statName(stat) + " " + amount);
            super.awardStat(stat, amount);
        }

        private static <T> String statName(Stat<T> stat) {
            var type = BuiltInRegistries.STAT_TYPE.getKey(stat.getType());
            var value = stat.getType().getRegistry().getKey(stat.getValue());
            return type + " " + value;
        }
    }

    /** A connection allocated without its constructor, which keeps what is sent to it. */
    private static final class Recorder extends ServerGamePacketListenerImpl {
        List<Packet<?>> packets;

        private Recorder() {
            super(null, null, null, null);
            throw new UnsupportedOperationException();
        }

        @Override public void send(Packet<?> packet) {
            packets.add(packet);
        }

        @Override public void send(Packet<?> packet, ChannelFutureListener listener) {
            packets.add(packet);
        }
    }

    /** Records every game event within reach of the target, by name, offset and source. */
    private static final class Listener implements GameEventListener {
        private final BlockPos target;
        private final Entity user;
        private final List<String> events;

        Listener(BlockPos target, Entity user, List<String> events) {
            this.target = target;
            this.user = user;
            this.events = events;
        }

        @Override public PositionSource getListenerSource() {
            return new BlockPositionSource(target);
        }

        @Override public int getListenerRadius() {
            return 16;
        }

        @Override public boolean handleGameEvent(ServerLevel level, Holder<GameEvent> event, GameEvent.Context context,
            Vec3 pos) {
            var name = event.unwrapKey().orElseThrow().identifier().toString();
            var block = BlockPos.containing(pos).subtract(target);
            var source = source(context.sourceEntity());
            events.add(name + " " + block.getX() + "," + block.getY() + "," + block.getZ() + " " + source);
            return true;
        }

        private String source(Entity entity) {
            if (entity == null)
                return "none";

            if (entity == user)
                return "player";

            return BuiltInRegistries.ENTITY_TYPE.getKey(entity.getType()).toString();
        }
    }
}
