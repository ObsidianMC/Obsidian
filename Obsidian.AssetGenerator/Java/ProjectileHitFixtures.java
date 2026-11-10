import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import com.mojang.authlib.GameProfile;
import io.netty.channel.ChannelFutureListener;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Holder;
import net.minecraft.core.SectionPos;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.network.protocol.Packet;
import net.minecraft.network.protocol.game.ClientboundBlockEventPacket;
import net.minecraft.network.protocol.game.ClientboundLevelEventPacket;
import net.minecraft.network.protocol.game.ClientboundSoundPacket;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ClientInformation;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.server.network.ServerGamePacketListenerImpl;
import net.minecraft.server.players.PlayerList;
import net.minecraft.stats.Stat;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EntitySpawnReason;
import net.minecraft.world.entity.ExperienceOrb;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.entity.projectile.FishingHook;
import net.minecraft.world.entity.projectile.Projectile;
import net.minecraft.world.entity.projectile.arrow.ThrownTrident;
import net.minecraft.world.entity.projectile.hurtingprojectile.AbstractHurtingProjectile;
import net.minecraft.world.entity.projectile.throwableitemprojectile.ThrowableItemProjectile;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.item.alchemy.PotionContents;
import net.minecraft.world.level.GameType;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.Property;
import net.minecraft.world.level.gameevent.BlockPositionSource;
import net.minecraft.world.level.gameevent.GameEvent;
import net.minecraft.world.level.gameevent.GameEventListener;
import net.minecraft.world.level.gameevent.PositionSource;
import net.minecraft.world.level.gamerules.GameRules;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.Vec3;

/**
 * Records what a projectile does when it hits a block: each case's projectile is spawned beside a block, already
 * moving at it, and the level is ticked one game tick at a time, on a headless dedicated server in a flat world of air.
 *
 * <p>Each case of the fixture is its input: the blocks set up around the target, the projectile, where it starts
 * (relative to the target) and its motion, whether it burns, the trident's enchantments, its owner, the weather, game
 * rules and the advancement criteria to register. After each tick the probe records the blocks of the area that
 * changed, the redstone signal the cell east of the target receives, the entities of the area, what a bystander
 * hears (sounds, level events and block events), the game events, the block writes in order with their flags, the
 * blocks destroyed, the criteria and the statistics awarded. Fields with nothing to report are left out.
 *
 * <p>Ticks call {@code ServerLevel.tick} directly, from one task on the server thread, so nothing else ticks in
 * between. The owner and the bystander are players the level knows of, but not entities in its sections, so
 * projectiles never hit them and they never tick.
 */
public final class ProjectileHitFixtures {
    // How far from the target the area that is cleared, compared and searched for entities reaches.
    private static final int Reach = 2;

    // Set without neighbor or shape updates and without drops, like the replay's setup.
    private static final int QuietFlags =
        Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE | Block.UPDATE_SUPPRESS_DROPS | Block.UPDATE_SKIP_ON_PLACE;

    // Ticks run after a case's blocks are cleared, so that work it left scheduled finds air.
    private static final int DrainTicks = 30;

    private static MinecraftServer server;

    // Positions in events are written relative to the target, which the listener and recorder share.
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
        var gson = new GsonBuilder().setPrettyPrinting().disableHtmlEscaping().create();
        Files.writeString(fixturePath, compact(gson.toJson(root)) + "\n", StandardCharsets.UTF_8);
        System.out.println("Wrote " + fixturePath);
        System.out.flush();

        // The world is thrown away, so skip the server's save on exit.
        Runtime.getRuntime().halt(0);
    }

    // Forces the chunks around the target and waits until they tick entities.
    private static void forceArea(JsonObject root) throws Exception {
        var level = server.overworld();
        var target = position(root.getAsJsonArray("target"));
        server.submit(() -> {
            for (int x = (target.getX() - 8) >> 4; x <= (target.getX() + 8) >> 4; x++) {
                for (int z = (target.getZ() - 8) >> 4; z <= (target.getZ() + 8) >> 4; z++)
                    level.setChunkForced(x, z, true);
            }
        }).join();

        var chunk = net.minecraft.world.level.ChunkPos.containing(target).pack();
        while (!server.submit(() -> level.shouldTickBlocksAt(chunk)
            && level.areEntitiesLoaded(chunk)
            && level.isPositionEntityTicking(target)).join()) {
            Thread.sleep(50);
        }
    }

    private static void capture(JsonObject root, Set<String> selected) throws Exception {
        var level = server.overworld();
        var target = position(root.getAsJsonArray("target"));
        origin = target;

        // Fixed rules: no time or weather passing, nothing spawning, and mob griefing off, so that explosions keep
        // their blocks and no drop is left to chance.
        var rules = server.getGameRules();
        rules.set(GameRules.ADVANCE_TIME, false, server);
        rules.set(GameRules.ADVANCE_WEATHER, false, server);
        rules.set(GameRules.SPAWN_MOBS, false, server);
        rules.set(GameRules.RANDOM_TICK_SPEED, 0, server);
        rules.set(GameRules.MOB_GRIEFING, false, server);

        var owner = new ProbePlayer(server, level, profile("Owner"));
        owner.connection = recorder(new ArrayList<>());
        var standing = root.getAsJsonArray("owner");
        owner.snapTo(
            target.getX() + standing.get(0).getAsDouble(),
            target.getY() + standing.get(1).getAsDouble(),
            target.getZ() + standing.get(2).getAsDouble(),
            0,
            0);
        owner.setGameMode(GameType.SURVIVAL);
        players(server.getPlayerList()).add(owner);

        // A second player beside the target hears what is broadcast around it.
        var bystander = new ProbePlayer(server, level, profile("Bystander"));
        var heard = new ArrayList<Packet<?>>();
        bystander.connection = recorder(heard);
        var listening = root.getAsJsonArray("bystander");
        bystander.snapTo(
            target.getX() + listening.get(0).getAsDouble(),
            target.getY() + listening.get(1).getAsDouble(),
            target.getZ() + listening.get(2).getAsDouble(),
            0,
            0);
        players(server.getPlayerList()).add(bystander);
        level.players().add(bystander);

        var gameEvents = new ArrayList<String>();
        var listener = new Listener(target, owner, gameEvents);
        level.getChunkAt(target).getListenerRegistry(SectionPos.blockToSectionCoord(target.getY())).register(listener);

        var thread = Thread.currentThread();
        var writes = new JsonArray();
        var destroyed = new JsonArray();
        observeWrites(level, thread, writes, destroyed);

        for (var element : root.getAsJsonArray("cases")) {
            var input = element.getAsJsonObject();
            if (!selected.isEmpty() && !selected.contains(input.get("name").getAsString()))
                continue;

            reset(level, target);
            applyRules(input, true);
            setWeather(level, input.has("thundering") && input.get("thundering").getAsBoolean());
            for (var setup : input.getAsJsonArray("setup")) {
                var placement = setup.getAsJsonObject();
                var pos = target.offset(position(placement.getAsJsonArray("offset")));
                level.setBlock(pos, state(placement.get("state").getAsString()), QuietFlags);
            }

            owner.getInventory().clearContent();
            if (input.has("ownerHolds")) {
                var held = BuiltInRegistries.ITEM.getValue(Identifier.parse(input.get("ownerHolds").getAsString()));
                owner.setItemInHand(InteractionHand.MAIN_HAND, new ItemStack(held));
            }

            var advancement = prepareCriteria(level, owner, input);
            var projectile = spawn(level, target, owner, input);

            var before = snapshot(level, target);
            var ticks = new JsonArray();
            for (int tick = 1; tick <= input.get("ticks").getAsInt(); tick++) {
                heard.clear();
                gameEvents.clear();
                owner.awarded.clear();
                writes.asList().clear();
                destroyed.asList().clear();
                var done = criteria(owner, advancement, input);

                level.tick(() -> true);

                var record = new JsonObject();
                record.addProperty("tick", tick);
                var changes = changes(level, target, before);
                if (!changes.isEmpty())
                    record.addProperty("changes", changes);

                before = snapshot(level, target);
                var signal = level.getBestNeighborSignal(target.east());
                if (signal != 0)
                    record.addProperty("signal", signal);

                addIfAny(record, "entities", entities(level, target, projectile));
                record(record, heard, gameEvents);
                addIfAny(record, "writes", writes.deepCopy());
                addIfAny(record, "destroyed", destroyed.deepCopy());

                var awarded = new JsonArray();
                for (var name : criteria(owner, advancement, input)) {
                    if (!done.contains(name))
                        awarded.add(name);
                }

                addIfAny(record, "criteria", awarded);
                addIfAny(record, "stats", strings(owner.awarded));
                ticks.add(record);
            }

            input.add("record", ticks);
            applyRules(input, false);
            System.out.println("Captured " + input.get("name").getAsString());
        }

        reset(level, target);
        players(server.getPlayerList()).remove(owner);
        players(server.getPlayerList()).remove(bystander);
        level.players().remove(bystander);
    }

    // Clears the area and its entities, and lets work the last case scheduled find air.
    private static void reset(ServerLevel level, BlockPos target) {
        clear(level, target);
        for (var entity : level.getEntities((Entity)null, area(target, Reach + 6), entity -> true))
            entity.discard();

        setWeather(level, false);
        for (int tick = 0; tick < DrainTicks; tick++)
            level.tick(() -> true);

        for (var entity : level.getEntities((Entity)null, area(target, Reach + 6), entity -> true))
            entity.discard();

        clear(level, target);
    }

    // The case's game rules, applied before it runs (or the defaults restored after it).
    private static void applyRules(JsonObject input, boolean apply) {
        if (!input.has("rules"))
            return;

        var rules = server.getGameRules();
        for (var rule : input.getAsJsonObject("rules").entrySet()) {
            var value = apply ? rule.getValue().getAsBoolean() : !rule.getValue().getAsBoolean();
            switch (rule.getKey()) {
                case "mob_griefing" -> rules.set(GameRules.MOB_GRIEFING, value, server);
                case "projectiles_can_break_blocks" -> rules.set(GameRules.PROJECTILES_CAN_BREAK_BLOCKS, value, server);
                default -> throw new IllegalArgumentException("Unknown rule " + rule.getKey());
            }
        }
    }

    private static void setWeather(ServerLevel level, boolean thundering) {
        var weather = level.getWeatherData();
        weather.setRaining(thundering);
        weather.setThundering(thundering);
        level.setRainLevel(thundering ? 1.0F : 0.0F);
        level.setThunderLevel(thundering ? 1.0F : 0.0F);
    }

    // The case's projectile at its start, moving at the target, owned by the owner unless the case says "none".
    private static Entity spawn(ServerLevel level, BlockPos target, ProbePlayer owner, JsonObject input)
        throws Exception {
        var id = input.get("projectile").getAsString();
        var type = BuiltInRegistries.ENTITY_TYPE.getValue(Identifier.parse(id));
        Entity projectile;
        if (id.equals("minecraft:trident")) {
            projectile = new ThrownTrident(level, 0, 0, 0, trident(level, input));
        } else if (id.equals("minecraft:fishing_bobber")) {
            projectile = new FishingHook(owner, level, 0, 0);
        } else {
            projectile = type.create(level, EntitySpawnReason.TRIGGERED);
        }

        if (input.has("potion") && projectile instanceof ThrowableItemProjectile thrown) {
            var potion = net.minecraft.world.item.alchemy.Potions.class.getField(input.get("potion").getAsString());
            @SuppressWarnings("unchecked")
            var holder = (Holder<net.minecraft.world.item.alchemy.Potion>)potion.get(null);
            thrown.setItem(PotionContents.createItemStack(Items.SPLASH_POTION, holder));
        }

        var start = input.getAsJsonArray("from");
        var motion = input.getAsJsonArray("motion");
        projectile.snapTo(
            target.getX() + start.get(0).getAsDouble(),
            target.getY() + start.get(1).getAsDouble(),
            target.getZ() + start.get(2).getAsDouble(),
            0,
            0);
        projectile.setDeltaMovement(
            motion.get(0).getAsDouble(),
            motion.get(1).getAsDouble(),
            motion.get(2).getAsDouble());
        if (projectile instanceof AbstractHurtingProjectile hurting && input.has("acceleration"))
            hurting.accelerationPower = input.get("acceleration").getAsDouble();

        if (input.has("noGravity"))
            projectile.setNoGravity(input.get("noGravity").getAsBoolean());

        var ownerless = input.has("owner") && input.get("owner").getAsString().equals("none");
        if (projectile instanceof Projectile shot)
            shot.setOwner(ownerless ? null : owner);

        if (input.has("burning") && input.get("burning").getAsBoolean())
            projectile.igniteForSeconds(100);

        // Eggs hatch one time in eight from their own random: seed it so that this one doesn't.
        if (id.equals("minecraft:egg"))
            projectile.getRandom().setSeed(input.get("seed").getAsLong());

        level.addFreshEntity(projectile);
        return projectile;
    }

    // The thrown trident's item, with the case's enchantments.
    private static ItemStack trident(ServerLevel level, JsonObject input) {
        var stack = new ItemStack(Items.TRIDENT);
        if (!input.has("enchantments"))
            return stack;

        var registry = level.registryAccess().lookupOrThrow(Registries.ENCHANTMENT);
        for (var entry : input.getAsJsonObject("enchantments").entrySet()) {
            var key = ResourceKey.create(Registries.ENCHANTMENT, Identifier.parse(entry.getKey()));
            stack.enchant(registry.getOrThrow(key), entry.getValue().getAsInt());
        }

        return stack;
    }

    private static void addIfAny(JsonObject record, String name, JsonArray values) {
        if (!values.isEmpty())
            record.add(name, values);
    }

    private static GameProfile profile(String name) {
        return new GameProfile(UUID.nameUUIDFromBytes(name.getBytes(StandardCharsets.UTF_8)), name);
    }

    // Registers the case's criteria for the owner, as block_use's probe does.
    private static net.minecraft.advancements.AdvancementHolder prepareCriteria(
        ServerLevel level, ProbePlayer owner, JsonObject input) throws Exception {
        if (!input.has("criteria"))
            return null;

        var definition = new JsonObject();
        definition.add("criteria", input.getAsJsonObject("criteria").deepCopy());
        var ops = level.registryAccess().createSerializationContext(com.mojang.serialization.JsonOps.INSTANCE);
        var value = net.minecraft.advancements.Advancement.CODEC.parse(ops, definition).getOrThrow();
        var id = Identifier.parse("probe:" + input.get("name").getAsString());
        var holder = new net.minecraft.advancements.AdvancementHolder(id, value);
        owner.getAdvancements().getOrStartProgress(holder);
        var register = net.minecraft.server.PlayerAdvancements.class.getDeclaredMethod(
            "registerListeners", net.minecraft.advancements.AdvancementHolder.class);
        register.setAccessible(true);
        register.invoke(owner.getAdvancements(), holder);
        return holder;
    }

    // The case's criteria the owner has so far.
    private static List<String> criteria(ProbePlayer owner, net.minecraft.advancements.AdvancementHolder advancement,
        JsonObject input) {
        var done = new ArrayList<String>();
        if (advancement == null)
            return done;

        var progress = owner.getAdvancements().getOrStartProgress(advancement);
        for (var name : input.getAsJsonObject("criteria").keySet()) {
            if (progress.getCriterion(name).isDone())
                done.add(name);
        }

        return done;
    }

    // The scratch classes call these observers only after a changed state is stored, before its side effects.
    private static void observeWrites(ServerLevel level, Thread thread, JsonArray writes, JsonArray destroyed)
        throws Exception {
        var parameters = new Class<?>[]{Object.class, Object.class, Object.class, int.class};
        net.minecraft.world.level.chunk.LevelChunk.class.getDeclaredMethod("obsidianObserveBlockUse", parameters);
        net.minecraft.world.level.Level.class.getDeclaredMethod("obsidianObserveBlockUse", parameters);
        for (var kind : List.of("writes", "destroyed")) {
            java.util.function.Consumer<Object[]> observer = values -> {
                if (values[0] != level || Thread.currentThread() != thread)
                    return;

                var pos = (BlockPos)values[1];
                var recorded = offset(pos) + "=" + text((BlockState)values[2]);
                if (kind.equals("writes")) {
                    writes.add(recorded + " flags " + values[3]);
                } else {
                    destroyed.add(recorded + " drops " + ((Integer)values[3] != 0));
                }
            };
            System.getProperties().put("obsidian.blockUse." + kind, observer);
        }
    }

    // The sounds, level events and block events the bystander received, and the game events, in order.
    private static void record(JsonObject output, List<Packet<?>> heard, List<String> gameEvents) {
        var sounds = new ArrayList<String>();
        var levelEvents = new ArrayList<String>();
        var blockEvents = new ArrayList<String>();
        for (var packet : heard) {
            if (packet instanceof ClientboundSoundPacket sound) {
                sounds.add(sound.getSound().value().location().toString());
            } else if (packet instanceof ClientboundLevelEventPacket event) {
                levelEvents.add(event.getType() + " " + offset(event.getPos()) + " " + event.getData());
            } else if (packet instanceof ClientboundBlockEventPacket event) {
                var block = BuiltInRegistries.BLOCK.getKey(event.getBlock()).toString();
                blockEvents.add(block + " " + event.getB0() + " " + event.getB1() + " " + offset(event.getPos()));
            }
        }

        addIfAny(output, "sounds", strings(sounds));
        addIfAny(output, "levelEvents", strings(levelEvents));
        addIfAny(output, "blockEvents", strings(blockEvents));
        addIfAny(output, "gameEvents", strings(new ArrayList<>(gameEvents)));
    }

    private static String offset(BlockPos pos) {
        var relative = pos.subtract(origin);
        return relative.getX() + "," + relative.getY() + "," + relative.getZ();
    }

    // The entities of the area, sorted: items as "item <id> x<count>", experience orbs by type alone (how vanilla
    // splits the experience is its own business), and others as "<type> <cell>", the cell relative to the target.
    // The case's projectile is "projectile <cell>".
    private static JsonArray entities(ServerLevel level, BlockPos target, Entity projectile) {
        var found = new ArrayList<String>();
        for (var entity : level.getEntities((Entity)null, area(target, Reach + 1), entity -> true)) {
            if (entity instanceof ItemEntity item) {
                found.add("item " + stack(item.getItem()));
            } else if (entity instanceof ExperienceOrb) {
                if (!found.contains("minecraft:experience_orb"))
                    found.add("minecraft:experience_orb");
            } else {
                var name = entity == projectile
                    ? "projectile"
                    : BuiltInRegistries.ENTITY_TYPE.getKey(entity.getType()).toString();
                found.add(name + " " + offset(entity.blockPosition()));
            }
        }

        found.sort(String::compareTo);
        return strings(found);
    }

    private static AABB area(BlockPos target, int reach) {
        return new AABB(target).inflate(reach);
    }

    // An item stack as "<id> x<count>".
    private static String stack(ItemStack stack) {
        return BuiltInRegistries.ITEM.getKey(stack.getItem()) + " x" + stack.getCount();
    }

    @SuppressWarnings("unchecked")
    private static List<ServerPlayer> players(PlayerList list) throws Exception {
        var field = PlayerList.class.getDeclaredField("players");
        field.setAccessible(true);
        return (List<ServerPlayer>)field.get(list);
    }

    // A game connection that keeps the packets sent to it instead of sending them.
    private static ServerGamePacketListenerImpl recorder(List<Packet<?>> packets) throws Exception {
        var unsafeField = Class.forName("sun.misc.Unsafe").getDeclaredField("theUnsafe");
        unsafeField.setAccessible(true);
        var unsafe = unsafeField.get(null);
        var allocate = unsafe.getClass().getMethod("allocateInstance", Class.class);
        var recorder = (Recorder)allocate.invoke(unsafe, Recorder.class);
        recorder.packets = packets;
        return recorder;
    }

    private static void clear(ServerLevel level, BlockPos target) {
        var air = Blocks.AIR.defaultBlockState();
        for (var pos : BlockPos.betweenClosed(target.offset(-Reach - 1, -Reach - 1, -Reach - 1),
            target.offset(Reach + 1, Reach + 1, Reach + 1))) {
            level.setBlock(pos, air, QuietFlags);
        }
    }

    private static List<BlockState> snapshot(ServerLevel level, BlockPos target) {
        var states = new ArrayList<BlockState>();
        for (var pos : compared(target))
            states.add(level.getBlockState(pos));

        return states;
    }

    private static String changes(ServerLevel level, BlockPos target, List<BlockState> before) {
        var changed = new ArrayList<String>();
        int index = 0;
        for (var pos : compared(target)) {
            var state = level.getBlockState(pos);
            if (state != before.get(index))
                changed.add(offset(pos) + "=" + text(state));

            index++;
        }

        return String.join(";", changed);
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
        // one task on the server thread. Easy difficulty: lightning starts no fire.
        var properties = String.join("\n",
            "level-type=minecraft\\:flat",
            "generator-settings={\"layers\"\\:[{\"block\"\\:\"minecraft\\:air\",\"height\"\\:1}],"
                + "\"biome\"\\:\"minecraft\\:plains\"}",
            "generate-structures=false",
            "spawn-protection=0",
            "difficulty=easy",
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
        private final Entity owner;
        private final List<String> events;

        Listener(BlockPos target, Entity owner, List<String> events) {
            this.target = target;
            this.owner = owner;
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

        // The event's source: the owner as "player", another entity by type, or "none".
        private String source(Entity entity) {
            if (entity == null)
                return "none";

            if (entity == owner)
                return "player";

            return BuiltInRegistries.ENTITY_TYPE.getKey(entity.getType()).toString();
        }
    }
}
