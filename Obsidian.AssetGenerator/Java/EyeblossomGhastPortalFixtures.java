import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonNull;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import java.lang.reflect.Field;
import java.lang.reflect.Proxy;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.Holder;
import net.minecraft.core.HolderLookup;
import net.minecraft.core.HolderSet;
import net.minecraft.core.particles.ParticleOptions;
import net.minecraft.core.particles.TrailParticleOption;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.sounds.SoundEvent;
import net.minecraft.sounds.SoundSource;
import net.minecraft.util.RandomSource;
import net.minecraft.world.Difficulty;
import net.minecraft.world.DifficultyInstance;
import net.minecraft.world.attribute.EnvironmentAttributeSystem;
import net.minecraft.world.clock.ClockInstance;
import net.minecraft.world.clock.ClockManager;
import net.minecraft.world.entity.AgeableMob;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EquipmentSlot;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.Property;
import net.minecraft.world.level.chunk.LevelChunk;
import net.minecraft.world.level.chunk.LevelChunkSection;
import net.minecraft.world.level.gameevent.GameEvent;
import net.minecraft.world.level.gamerules.GameRules;
import net.minecraft.world.level.levelgen.PositionalRandomFactory;
import net.minecraft.world.level.material.Fluid;
import net.minecraft.world.level.material.FluidState;
import net.minecraft.world.level.redstone.NeighborUpdater;
import net.minecraft.world.level.storage.LevelData;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.Vec3;
import net.minecraft.world.timeline.Timeline;
import net.minecraft.world.ticks.LevelTicks;
import net.minecraft.world.ticks.TickPriority;

/**
 * Runs one vanilla routine of an eyeblossom, a potted eyeblossom, a dried ghast or a nether portal against a bounded,
 * explicitly supplied block view, with a seeded random that records every draw, and records what it did.
 *
 * Usage: EyeblossomGhastPortalFixtures <fixture.json> [name+name...]. Each case's inputs (call, setup, origin, state,
 * seed, pending, dimension, dayTime, difficulty, spawnMobs, spawnMonsters, playerNear) are kept; everything else is
 * replaced. With names, only those cases are recorded.
 */
public final class EyeblossomGhastPortalFixtures {
    private static final String NOTES = "Vanilla eyeblossom, potted eyeblossom, dried ghast and nether portal "
        + "routines, recorded by the asset generator's EyeblossomGhastPortalFixtures.java against a block view of "
        + "the case's setup (air elsewhere, void air outside the build height of -64 to 319), with a legacy random "
        + "seeded with seed (0 by default) as both the routine's random and the level's. call is randomTick (the "
        + "origin's state, only when its isRandomlyTicking holds, which ticks records), tick (the origin's scheduled "
        + "tick) or set (Level.setBlock of state at the origin with UPDATE_ALL). dimension (minecraft:overworld by default) gives the dimension type's attributes and its "
        + "timelines, sampled with every clock at dayTime (0 by default); biomes add nothing. difficulty (normal by "
        + "default), spawnMobs and spawnMonsters (true by default) are the level's; playerNear (true by default) "
        + "answers anyPlayerCloseEnoughForSpawning. The level's game time is 20, chunks have no inhabited time, and "
        + "getCurrentDifficultyAt is the DifficultyInstance of the difficulty at that game time. Writes store the "
        + "block and, as Level.setBlock does, run the new state's onPlace and then, unless UPDATE_KNOWN_SHAPE is "
        + "set, the neighbours' shape updates; neighbour notifications are not run. draws lists each draw in order; "
        + "writes the block changes with their flags; scheduled the block and fluid ticks scheduled with their "
        + "delays (a block tick counts as pending for hasScheduledTick, as do the case's pending ticks); events the "
        + "level events; gameEvents the game events with the state they carry; sounds every sound with its position "
        + "(or the entity it follows), source, volume and pitch (null when the pitch comes from a mob's own random, "
        + "as a mob's ambient sound's does); particles the particles sent; entities the entities added, with their "
        + "rotation, age, portal cooldown, main hand and spawn flags; destroyed the blocks destroyed and whether "
        + "they drop. after holds the final state of every setup or written position. The neighbour scan an "
        + "eyeblossom makes reads the view through chunk sections, so it visits blocks in vanilla's section order.";

    private static final List<String> OUTPUTS = List.of(
        "ticks", "draws", "writes", "scheduled", "events", "gameEvents", "sounds", "particles",
        "entities", "destroyed", "after");

    public static void main(String[] arguments) throws Exception {
        var version = VanillaDumper.bootstrap();
        var path = Path.of(arguments[0]);
        var root = JsonParser.parseString(Files.readString(path)).getAsJsonObject();
        root.addProperty("_notes", NOTES);
        root.addProperty("version", version);

        var selected = arguments.length > 1 ? Set.of(arguments[1].split("\\+")) : null;
        var captured = 0;
        for (var entry : root.getAsJsonArray("cases")) {
            var input = entry.getAsJsonObject();
            if (selected != null && !selected.contains(input.get("name").getAsString())) {
                continue;
            }

            capture(input);
            captured++;
        }

        if (selected != null && captured != selected.size()) {
            throw new IllegalArgumentException("Every selected case must exist and be named once");
        }

        var gson = new GsonBuilder().serializeNulls().setPrettyPrinting().disableHtmlEscaping().create();
        Files.writeString(path, gson.toJson(root) + "\n");
    }

    private static void capture(JsonObject input) throws Exception {
        for (var key : OUTPUTS) {
            input.remove(key);
        }

        var seed = input.has("seed") ? input.get("seed").getAsLong() : 0L;
        var random = new RecordingRandom(seed);
        var level = ProbeLevel.allocate(random);
        configure(input, level);

        var touched = new LinkedHashSet<BlockPos>();
        for (var placement : input.getAsJsonArray("setup")) {
            var value = placement.getAsJsonObject();
            var pos = position(value.getAsJsonArray("pos"));
            level.blocks.put(pos, state(value.get("state").getAsString()));
            touched.add(pos);
        }

        // Block ticks already pending before the routine, which hasScheduledTick sees but scheduled doesn't list.
        if (input.has("pending")) {
            for (var tick : input.getAsJsonArray("pending")) {
                level.pending.add(tick.getAsJsonObject());
            }
        }

        var origin = position(input.getAsJsonArray("origin"));
        var call = input.get("call").getAsString();
        var current = level.getBlockState(origin);
        switch (call) {
            case "randomTick" -> {
                // The level only random-ticks a state whose isRandomlyTicking holds, so the others draw nothing.
                var ticks = current.isRandomlyTicking();
                input.addProperty("ticks", ticks);
                if (ticks) {
                    current.randomTick(level, origin, random);
                }
            }
            case "tick" -> current.tick(level, origin, random);
            case "set" -> level.setBlock(origin, state(input.get("state").getAsString()), Block.UPDATE_ALL, 512);
            default -> throw new IllegalArgumentException("Unknown call " + call);
        }

        input.add("draws", random.draws);
        input.add("writes", level.writes);
        input.add("scheduled", level.scheduled);
        input.add("events", level.events);
        input.add("gameEvents", level.gameEvents);
        input.add("sounds", level.sounds);
        input.add("particles", level.particles);
        input.add("entities", level.entities());
        input.add("destroyed", level.destroyed);

        for (var write : level.writes) {
            touched.add(position(write.getAsJsonObject().getAsJsonArray("pos")));
        }

        Comparator<BlockPos> order = Comparator.comparingInt((BlockPos pos) -> pos.getY())
            .thenComparingInt(pos -> pos.getZ())
            .thenComparingInt(pos -> pos.getX());
        var after = new JsonArray();
        touched.stream()
            .sorted(order)
            .forEach(pos -> after.add(placement(pos, level.getBlockState(pos))));
        input.add("after", after);
    }

    // The case's dimension type (its attributes, then its timelines at dayTime), difficulty, rules and players.
    private static void configure(JsonObject input, ProbeLevel level) throws Exception {
        var dimensionName = input.has("dimension") ? input.get("dimension").getAsString() : "minecraft:overworld";
        var registries = (HolderLookup.Provider)VanillaDumper.vanillaRegistries();
        var key = ResourceKey.create(Registries.DIMENSION_TYPE, Identifier.parse(dimensionName));
        var dimension = registries.lookupOrThrow(Registries.DIMENSION_TYPE).getOrThrow(key).value();

        var dayTime = input.has("dayTime") ? input.get("dayTime").getAsLong() : 0L;
        ClockManager clocks = clock -> new FixedClock(dayTime);
        var environment = EnvironmentAttributeSystem.builder().addConstantLayer(dimension.attributes());
        var timelines = registries.lookupOrThrow(Registries.TIMELINE);
        for (var timeline : timelines(dimension.timelines(), timelines)) {
            environment.addTimelineLayer(timeline, clocks);
        }

        level.environment = environment.build();

        var difficulty = input.has("difficulty") ? input.get("difficulty").getAsString() : "normal";
        level.difficulty = Difficulty.byName(difficulty);
        level.rules.set(GameRules.SPAWN_MOBS, flag(input, "spawnMobs"), null);
        level.rules.set(GameRules.SPAWN_MONSTERS, flag(input, "spawnMonsters"), null);
        level.playerNear = flag(input, "playerNear");
    }

    // A dimension type's timelines in their tag's order. The registries are built without data pack tags, so a tag is
    // read from vanilla's own tag files, whose nested tags expand in place.
    private static List<Holder<Timeline>> timelines(
        HolderSet<Timeline> set,
        HolderLookup.RegistryLookup<Timeline> lookup) throws Exception {
        var tag = set.unwrapKey();
        if (tag.isEmpty()) {
            return set.stream().toList();
        }

        var result = new ArrayList<Holder<Timeline>>();
        expand(tag.get().location(), lookup, result);
        return result;
    }

    private static void expand(Identifier tag, HolderLookup.RegistryLookup<Timeline> lookup,
        List<Holder<Timeline>> result) throws Exception {
        var path = "data/" + tag.getNamespace() + "/tags/timeline/" + tag.getPath() + ".json";
        try (var stream = EyeblossomGhastPortalFixtures.class.getClassLoader().getResourceAsStream(path)) {
            var file = JsonParser.parseString(new String(stream.readAllBytes())).getAsJsonObject();
            for (var value : file.getAsJsonArray("values")) {
                var entry = value.getAsString();
                if (entry.startsWith("#")) {
                    expand(Identifier.parse(entry.substring(1)), lookup, result);
                } else {
                    var key = ResourceKey.create(Registries.TIMELINE, Identifier.parse(entry));
                    result.add(lookup.getOrThrow(key));
                }
            }
        }
    }

    private static boolean flag(JsonObject input, String name) {
        return !input.has(name) || input.get(name).getAsBoolean();
    }

    private record FixedClock(long ticks) implements ClockInstance {
        @Override public long totalTicks() {
            return ticks;
        }

        @Override public float partialTick() {
            return 0;
        }

        @Override public float rate() {
            return 1;
        }

        @Override public boolean isPaused() {
            return false;
        }
    }

    private static Field field(Class<?> type, String name) throws Exception {
        var field = type.getDeclaredField(name);
        field.setAccessible(true);
        return field;
    }

    private static Object allocate(Class<?> type) throws Exception {
        var field = Class.forName("sun.misc.Unsafe").getDeclaredField("theUnsafe");
        field.setAccessible(true);
        var unsafe = field.get(null);
        return unsafe.getClass().getMethod("allocateInstance", Class.class).invoke(unsafe, type);
    }

    private static JsonObject placement(BlockPos pos, BlockState state) {
        var result = new JsonObject();
        result.add("pos", coordinates(pos));
        result.addProperty("state", name(state));
        return result;
    }

    private static BlockPos position(JsonArray values) {
        return new BlockPos(values.get(0).getAsInt(), values.get(1).getAsInt(), values.get(2).getAsInt());
    }

    private static JsonArray coordinates(BlockPos position) {
        var result = new JsonArray();
        result.add(position.getX());
        result.add(position.getY());
        result.add(position.getZ());
        return result;
    }

    private static JsonArray coordinates(double x, double y, double z) {
        var result = new JsonArray();
        result.add(x);
        result.add(y);
        result.add(z);
        return result;
    }

    private static String name(BlockState state) {
        return state.toString().replace("Block{", "").replace("}", "");
    }

    private static BlockState state(String text) {
        var bracket = text.indexOf('[');
        var name = bracket < 0 ? text : text.substring(0, bracket);
        var block = BuiltInRegistries.BLOCK.getValue(Identifier.parse(name));
        var state = block.defaultBlockState();
        if (bracket < 0) {
            return state;
        }

        for (var pair : text.substring(bracket + 1, text.length() - 1).split(",")) {
            var parts = pair.split("=");
            var property = block.getStateDefinition().getProperty(parts[0]);
            state = withProperty(state, property, parts[1]);
        }

        return state;
    }

    private static <T extends Comparable<T>> BlockState withProperty(BlockState state, Property<T> property,
        String value) {
        return state.setValue(property, property.getValue(value).orElseThrow());
    }

    // Whether the sound being played is a mob's ambient sound, whose pitch vanilla draws from the mob's own random.
    private static boolean fromMobVoice() {
        return StackWalker.getInstance().walk(frames -> frames.anyMatch(frame ->
            frame.getClassName().equals(Mob.class.getName()) && frame.getMethodName().equals("playAmbientSound")));
    }

    /** A legacy (java.util.Random) source, as the level's, that records each draw and its result. */
    private static final class RecordingRandom implements RandomSource {
        private final RandomSource source;
        final JsonArray draws = new JsonArray();

        RecordingRandom(long seed) {
            this.source = RandomSource.create(seed);
        }

        private void record(String call, Integer bound, Object value) {
            var draw = new JsonObject();
            draw.addProperty("call", call);
            if (bound != null) {
                draw.addProperty("bound", bound);
            }

            if (value instanceof Boolean flag) {
                draw.addProperty("value", flag);
            } else {
                draw.addProperty("value", (Number)value);
            }

            draws.add(draw);
        }

        @Override public RandomSource fork() {
            throw new UnsupportedOperationException("fork");
        }

        @Override public PositionalRandomFactory forkPositional() {
            throw new UnsupportedOperationException("forkPositional");
        }

        @Override public void setSeed(long seed) {
            throw new UnsupportedOperationException("setSeed");
        }

        @Override public int nextInt() {
            var value = source.nextInt();
            record("nextInt", null, value);
            return value;
        }

        @Override public int nextInt(int bound) {
            var value = source.nextInt(bound);
            record("nextInt", bound, value);
            return value;
        }

        @Override public long nextLong() {
            var value = source.nextLong();
            record("nextLong", null, value);
            return value;
        }

        @Override public boolean nextBoolean() {
            var value = source.nextBoolean();
            record("nextBoolean", null, value);
            return value;
        }

        @Override public float nextFloat() {
            var value = source.nextFloat();
            record("nextFloat", null, value);
            return value;
        }

        @Override public double nextDouble() {
            var value = source.nextDouble();
            record("nextDouble", null, value);
            return value;
        }

        @Override public double nextGaussian() {
            var value = source.nextGaussian();
            record("nextGaussian", null, value);
            return value;
        }
    }

    /** Pending block ticks as the probe's level recorded them, for vanilla's hasScheduledTick checks. */
    private static final class ProbeBlockTicks extends LevelTicks<Block> {
        private final ProbeLevel level;

        ProbeBlockTicks(ProbeLevel level) {
            super(pos -> true);
            this.level = level;
        }

        @Override public boolean hasScheduledTick(BlockPos pos, Block type) {
            var name = BuiltInRegistries.BLOCK.getKey(type).toString();
            var ticks = new JsonArray();
            ticks.addAll(level.pending);
            ticks.addAll(level.scheduled);
            for (var entry : ticks) {
                var tick = entry.getAsJsonObject();
                if (position(tick.getAsJsonArray("pos")).equals(pos) && tick.get("type").getAsString().equals(name)) {
                    return true;
                }
            }

            return false;
        }
    }

    /**
     * The block view: blocks from the case (air elsewhere), the case's environment, difficulty and rules, and the
     * level random being the routine's random, as ServerLevel passes its own random to block ticks.
     */
    private static final class ProbeLevel extends ServerLevel {
        Map<BlockPos, BlockState> blocks;
        Map<Long, ProbeChunk> chunks;
        JsonArray writes;
        JsonArray pending;
        JsonArray scheduled;
        JsonArray events;
        JsonArray gameEvents;
        JsonArray sounds;
        JsonArray particles;
        List<Entity> added;
        JsonArray destroyed;
        RandomSource tickRandom;
        GameRules rules;
        LevelTicks<Block> blockTicks;
        EnvironmentAttributeSystem environment;
        Difficulty difficulty;
        boolean playerNear;
        LevelData data;

        private ProbeLevel() {
            super(null, null, null, null, null, null, false, 0, List.of(), false);
            throw new UnsupportedOperationException();
        }

        static ProbeLevel allocate(RandomSource random) throws Exception {
            var level = (ProbeLevel)EyeblossomGhastPortalFixtures.allocate(ProbeLevel.class);
            level.blocks = new LinkedHashMap<>();
            level.chunks = new HashMap<>();
            level.writes = new JsonArray();
            level.pending = new JsonArray();
            level.scheduled = new JsonArray();
            level.events = new JsonArray();
            level.gameEvents = new JsonArray();
            level.sounds = new JsonArray();
            level.particles = new JsonArray();
            level.added = new ArrayList<>();
            level.destroyed = new JsonArray();
            level.tickRandom = random;
            level.rules = new GameRules(
                List.of(GameRules.ENTITY_DROPS, GameRules.SPAWN_MOBS, GameRules.SPAWN_MONSTERS));
            level.blockTicks = new ProbeBlockTicks(level);
            level.environment = EnvironmentAttributeSystem.builder().build();
            level.difficulty = Difficulty.NORMAL;
            level.data = (LevelData)Proxy.newProxyInstance(LevelData.class.getClassLoader(),
                new Class<?>[] { LevelData.class }, (proxy, method, arguments) -> switch (method.getName()) {
                    case "getDifficulty" -> level.difficulty;
                    case "getGameTime" -> level.getGameTime();
                    case "isHardcore", "isDifficultyLocked" -> false;
                    default -> throw new UnsupportedOperationException(method.getName());
                });

            // Mobs' path finders ask the server whether to capture debug paths: give the level an allocated server
            // that has only its debug subscribers, with no one subscribed.
            var server = (net.minecraft.server.MinecraftServer)EyeblossomGhastPortalFixtures.allocate(
                net.minecraft.server.dedicated.DedicatedServer.class);
            field(net.minecraft.server.MinecraftServer.class, "debugSubscribers")
                .set(server, new net.minecraft.util.debug.ServerDebugSubscribers(server));
            field(ServerLevel.class, "server").set(level, server);

            // Sound seeds come from the level's own generator, not its random: give it one apart from the recorder.
            field(net.minecraft.world.level.Level.class, "soundSeedGenerator").set(level, RandomSource.create(0));
            return level;
        }

        @Override public RandomSource getRandom() {
            return tickRandom;
        }

        // A world with vanilla's default features, as a new world has.
        @Override public net.minecraft.world.flag.FeatureFlagSet enabledFeatures() {
            return net.minecraft.world.flag.FeatureFlags.DEFAULT_FLAGS;
        }

        @Override public GameRules getGameRules() {
            return rules;
        }

        @Override public LevelData getLevelData() {
            return data;
        }

        @Override public Difficulty getDifficulty() {
            return difficulty;
        }

        @Override public boolean anyPlayerCloseEnoughForSpawning(BlockPos pos) {
            return playerNear;
        }

        // The block view has no chunk to have been inhabited, and at game time 20 the moon adds nothing.
        @Override public DifficultyInstance getCurrentDifficultyAt(BlockPos pos) {
            return new DifficultyInstance(difficulty, getGameTime(), 0L, 0.0F);
        }

        @Override public EnvironmentAttributeSystem environmentAttributes() {
            return environment;
        }

        @Override public LevelTicks<Block> getBlockTicks() {
            return blockTicks;
        }

        @Override public int getMinY() {
            return -64;
        }

        @Override public int getHeight() {
            return 384;
        }

        @Override public int getNextEntityId() {
            return 1;
        }

        @Override public long getGameTime() {
            return 20;
        }

        @Override public BlockState getBlockState(BlockPos pos) {
            if (isOutsideBuildHeight(pos)) {
                return Blocks.VOID_AIR.defaultBlockState();
            }

            return blocks.getOrDefault(pos, Blocks.AIR.defaultBlockState());
        }

        @Override public FluidState getFluidState(BlockPos pos) {
            return getBlockState(pos).getFluidState();
        }

        // The sections vanilla's block scans read: each answers from the view, so the scan keeps its own order.
        @Override public LevelChunk getChunk(int x, int z) {
            return chunks.computeIfAbsent(((long)x << 32) | (z & 0xFFFFFFFFL), key -> ProbeChunk.of(this, x, z));
        }

        // Level.setBlock as far as these routines see it: outside the build height nothing changes; otherwise the
        // block is stored, the new state's onPlace runs, and unless UPDATE_KNOWN_SHAPE is set its neighbours' shapes
        // update.
        @Override public boolean setBlock(BlockPos pos, BlockState state, int flags, int limit) {
            if (isOutsideBuildHeight(pos)) {
                return false;
            }

            var old = getBlockState(pos);
            if (old == state) {
                return false;
            }

            blocks.put(pos.immutable(), state);

            var write = new JsonObject();
            write.add("pos", coordinates(pos));
            write.addProperty("state", name(state));
            write.addProperty("flags", flags);
            writes.add(write);

            if ((flags & Block.UPDATE_SKIP_ON_PLACE) == 0) {
                state.onPlace(this, pos.immutable(), old, (flags & Block.UPDATE_MOVE_BY_PISTON) != 0);
            }

            if ((flags & Block.UPDATE_KNOWN_SHAPE) == 0 && limit > 0) {
                var neighbourFlags = flags & ~(Block.UPDATE_NEIGHBORS | Block.UPDATE_SUPPRESS_DROPS);
                old.updateIndirectNeighbourShapes(this, pos, neighbourFlags, limit - 1);
                state.updateNeighbourShapes(this, pos, neighbourFlags, limit - 1);
                state.updateIndirectNeighbourShapes(this, pos, neighbourFlags, limit - 1);
            }

            return true;
        }

        @Override public boolean destroyBlock(BlockPos pos, boolean drops, Entity breaker, int limit) {
            var state = getBlockState(pos);
            if (state.isAir()) {
                return false;
            }

            var destroyedBlock = new JsonObject();
            destroyedBlock.add("pos", coordinates(pos));
            destroyedBlock.addProperty("state", name(state));
            destroyedBlock.addProperty("drops", drops);
            destroyed.add(destroyedBlock);
            return setBlock(pos, getFluidState(pos).createLegacyBlock(), Block.UPDATE_ALL, limit);
        }

        @Override public void neighborShapeChanged(Direction direction, BlockPos pos, BlockPos neighbour,
            BlockState neighbourState, int flags, int limit) {
            NeighborUpdater.executeShapeUpdate(this, direction, pos, neighbour, neighbourState, flags, limit);
        }

        @Override public void scheduleTick(BlockPos pos, Block type, int delay, TickPriority priority) {
            schedule(pos, BuiltInRegistries.BLOCK.getKey(type).toString(), delay);
        }

        @Override public void scheduleTick(BlockPos pos, Block type, int delay) {
            schedule(pos, BuiltInRegistries.BLOCK.getKey(type).toString(), delay);
        }

        @Override public void scheduleTick(BlockPos pos, Fluid type, int delay, TickPriority priority) {
            schedule(pos, BuiltInRegistries.FLUID.getKey(type).toString(), delay);
        }

        @Override public void scheduleTick(BlockPos pos, Fluid type, int delay) {
            schedule(pos, BuiltInRegistries.FLUID.getKey(type).toString(), delay);
        }

        private void schedule(BlockPos pos, String type, int delay) {
            var tick = new JsonObject();
            tick.add("pos", coordinates(pos));
            tick.addProperty("type", type);
            tick.addProperty("delay", delay);
            scheduled.add(tick);
        }

        @Override public void levelEvent(Entity source, int event, BlockPos pos, int data) {
            var recorded = new JsonObject();
            recorded.addProperty("type", event);
            recorded.add("pos", coordinates(pos));
            recorded.addProperty("data", data);
            events.add(recorded);
        }

        @Override public void gameEvent(Holder<GameEvent> event, Vec3 pos, GameEvent.Context context) {
            var recorded = new JsonObject();
            recorded.addProperty("type", event.unwrapKey().orElseThrow().identifier().toString());
            recorded.add("pos", coordinates(pos.x, pos.y, pos.z));
            var affected = context.affectedState();
            recorded.addProperty("state", affected == null ? null : name(affected));
            gameEvents.add(recorded);
        }

        @Override public void playSeededSound(Entity except, double x, double y, double z, Holder<SoundEvent> sound,
            SoundSource source, float volume, float pitch, long seed) {
            var recorded = sound(sound, source, volume, pitch);
            recorded.add("pos", coordinates(x, y, z));
            sounds.add(recorded);
        }

        @Override public void playSeededSound(Entity except, Entity sourceEntity, Holder<SoundEvent> sound,
            SoundSource source, float volume, float pitch, long seed) {
            var recorded = sound(sound, source, volume, pitch);
            recorded.addProperty("entity", BuiltInRegistries.ENTITY_TYPE.getKey(sourceEntity.getType()).toString());
            sounds.add(recorded);
        }

        private static JsonObject sound(Holder<SoundEvent> sound, SoundSource source, float volume, float pitch) {
            var recorded = new JsonObject();
            recorded.addProperty("sound", sound.value().location().toString());
            recorded.addProperty("source", source.getName());
            recorded.addProperty("volume", volume);
            if (fromMobVoice()) {
                recorded.add("pitch", JsonNull.INSTANCE);
            } else {
                recorded.addProperty("pitch", pitch);
            }

            return recorded;
        }

        @Override public <T extends ParticleOptions> int sendParticles(T particle, boolean overrideLimiter,
            boolean alwaysShow, double x, double y, double z, int count, double xDist, double yDist, double zDist,
            double xSpeed, double ySpeed, double zSpeed,
            net.minecraft.network.protocol.game.ClientboundLevelParticlesPacket.RandomizationType randomizationType) {
            var recorded = new JsonObject();
            recorded.addProperty("type", BuiltInRegistries.PARTICLE_TYPE.getKey(particle.getType()).toString());
            recorded.add("pos", coordinates(x, y, z));
            recorded.addProperty("count", count);
            recorded.add("dist", coordinates(xDist, yDist, zDist));
            recorded.add("speed", coordinates(xSpeed, ySpeed, zSpeed));
            recorded.addProperty("overrideLimiter", overrideLimiter);
            recorded.addProperty("alwaysShow", alwaysShow);
            if (particle instanceof TrailParticleOption trail) {
                var target = trail.target();
                recorded.add("target", coordinates(target.x, target.y, target.z));
                recorded.addProperty("color", trail.color());
                recorded.addProperty("duration", trail.duration());
            }

            particles.add(recorded);
            return 1;
        }

        // No entities stand in the view, so the ones the routines look for (a zombie's chicken to ride) find none.
        @Override public List<Entity> getEntities(Entity except, AABB box,
            java.util.function.Predicate<? super Entity> predicate) {
            return List.of();
        }

        @Override public boolean addFreshEntity(Entity entity) {
            added.add(entity);
            return true;
        }

        // The entities added, read once the routine is done.
        JsonArray entities() {
            var result = new JsonArray();
            for (var entity : added) {
                var recorded = new JsonObject();
                recorded.addProperty("type", BuiltInRegistries.ENTITY_TYPE.getKey(entity.getType()).toString());
                var position = entity.position();
                recorded.add("pos", coordinates(position.x, position.y, position.z));
                recorded.addProperty("yaw", entity.getYRot());
                recorded.addProperty("headYaw", entity.getYHeadRot());
                recorded.addProperty("pitch", entity.getXRot());
                recorded.addProperty("portalCooldown", entity.getPortalCooldown());
                if (entity instanceof AgeableMob ageable) {
                    recorded.addProperty("age", ageable.getAge());
                }

                if (entity instanceof Mob mob) {
                    recorded.addProperty("baby", mob.isBaby());
                    var held = mob.getItemBySlot(EquipmentSlot.MAINHAND);
                    recorded.addProperty("mainHand", BuiltInRegistries.ITEM.getKey(held.getItem()).toString());
                    recorded.addProperty("enchanted", held.isEnchanted());
                    recorded.addProperty("leftHanded", mob.isLeftHanded());
                    recorded.addProperty("canPickUpLoot", mob.canPickUpLoot());
                    recorded.addProperty("vehicle", entity.getVehicle() == null ? null
                        : BuiltInRegistries.ENTITY_TYPE.getKey(entity.getVehicle().getType()).toString());
                }

                result.add(recorded);
            }

            return result;
        }
    }

    // A chunk of the view whose sections read the view's blocks, at their place in the world.
    private static final class ProbeChunk extends LevelChunk {
        ProbeLevel level;
        int x;
        int z;

        private ProbeChunk() {
            super(null, null);
        }

        static ProbeChunk of(ProbeLevel level, int x, int z) {
            try {
                var chunk = (ProbeChunk)EyeblossomGhastPortalFixtures.allocate(ProbeChunk.class);
                chunk.level = level;
                chunk.x = x;
                chunk.z = z;
                return chunk;
            } catch (Exception exception) {
                throw new RuntimeException(exception);
            }
        }

        @Override public int getSectionIndexFromSectionY(int sectionY) {
            return sectionY;
        }

        @Override public LevelChunkSection getSection(int sectionY) {
            try {
                var section = (ProbeSection)EyeblossomGhastPortalFixtures.allocate(ProbeSection.class);
                section.level = level;
                section.origin = new BlockPos(x * 16, sectionY * 16, z * 16);
                return section;
            } catch (Exception exception) {
                throw new RuntimeException(exception);
            }
        }
    }

    private static final class ProbeSection extends LevelChunkSection {
        ProbeLevel level;
        BlockPos origin;

        private ProbeSection() {
            super(null, null);
        }

        @Override public boolean maybeHas(java.util.function.Predicate<BlockState> predicate) {
            return true;
        }

        @Override public BlockState getBlockState(int x, int y, int z) {
            return level.getBlockState(origin.offset(x, y, z));
        }
    }
}
