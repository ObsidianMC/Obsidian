import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.Holder;
import net.minecraft.core.HolderLookup;
import net.minecraft.core.RegistryAccess;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.dedicated.DedicatedServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.sounds.SoundEvent;
import net.minecraft.sounds.SoundSource;
import net.minecraft.util.RandomSource;
import net.minecraft.util.debug.ServerDebugSubscribers;
import net.minecraft.world.attribute.EnvironmentAttributeSystem;
import net.minecraft.world.attribute.EnvironmentAttributes;
import net.minecraft.world.clock.ClockInstance;
import net.minecraft.world.entity.AgeableMob;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.animal.turtle.Turtle;
import net.minecraft.world.flag.FeatureFlagSet;
import net.minecraft.world.flag.FeatureFlags;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.Property;
import net.minecraft.world.level.dimension.DimensionType;
import net.minecraft.world.level.gameevent.GameEvent;
import net.minecraft.world.level.levelgen.PositionalRandomFactory;
import net.minecraft.world.level.material.Fluid;
import net.minecraft.world.level.material.FluidState;
import net.minecraft.world.level.redstone.NeighborUpdater;
import net.minecraft.world.phys.Vec3;
import net.minecraft.world.ticks.TickPriority;
import net.minecraft.world.timeline.Timeline;

/**
 * Calls a vanilla block's own routines (a random tick, or being set with every update) against a bounded block view,
 * with a seeded random that records every draw and an environment attribute system built from the dimension type's
 * attributes and timelines at the case's clock time. Also samples the gameplay/turtle_egg_hatch_chance attribute at
 * given times. It records the turtle eggs fixture.
 *
 * Usage: HatchingFixtures <fixture.json>. Each case's name, seed, dimension, time, setup, origin and place are its
 * input; ticks, draws, writes, scheduled, sounds, levelEvents, gameEvents, entities and after are replaced. Each
 * chance sample's dimension and time are its input; chance is replaced.
 */
public final class HatchingFixtures {
    private static final String NOTES = "Vanilla 26.3 blocks' own routines, recorded by the asset generator's "
        + "HatchingFixtures.java against a block view of the case's setup (air elsewhere). A case without place "
        + "random-ticks the origin's state if its isRandomlyTicking holds; a case with place sets that state at the "
        + "origin with every update, as /setblock does. The random is a legacy random seeded with seed, as both the "
        + "tick's random and the level's. The level's environment attributes are the dimension type's own "
        + "attributes and its timelines (from the dimension type's timeline tag) on a clock at time; biomes and "
        + "weather add no layer. draws lists each draw in order. writes are the block changes in order with their "
        + "update flags; each runs the new state's onPlace as a chunk does, and the neighbour shape updates it "
        + "causes. scheduled lists the block and fluid ticks scheduled, with their delays. sounds, levelEvents and "
        + "gameEvents are what the level was asked to play or emit, in order (sounds without their seed, which "
        + "vanilla draws from another random). entities are the entities added, with their position, yaw, age, "
        + "home and persistence. after holds the final state of every setup or written position. chances samples "
        + "gameplay/turtle_egg_hatch_chance at the centre of block 0, 64, 0 for a dimension and a clock time.";

    public static void main(String[] arguments) throws Exception {
        var version = VanillaDumper.bootstrap();
        var registries = (HolderLookup.Provider) VanillaDumper.vanillaRegistries();
        var path = Path.of(arguments[0]);
        var root = JsonParser.parseString(Files.readString(path)).getAsJsonObject();
        root.addProperty("_notes", NOTES);
        root.addProperty("version", version);

        for (var entry : root.getAsJsonArray("cases")) {
            capture(entry.getAsJsonObject(), registries);
        }

        var chances = root.has("chances") ? root.getAsJsonArray("chances") : new JsonArray();
        for (var entry : chances) {
            var sample = entry.getAsJsonObject();
            var attributes = attributes(registries, sample.get("dimension").getAsString(), sample.get("time").getAsLong());
            var chance = attributes.getValue(EnvironmentAttributes.TURTLE_EGG_HATCH_CHANCE, new BlockPos(0, 64, 0));
            sample.addProperty("chance", chance);
        }

        var gson = new GsonBuilder().serializeNulls().setPrettyPrinting().disableHtmlEscaping().create();
        Files.writeString(path, gson.toJson(root) + "\n");
    }

    private static void capture(JsonObject input, HolderLookup.Provider registries) throws Exception {
        for (var key : List.of("ticks", "draws", "writes", "scheduled", "sounds", "levelEvents", "gameEvents", "entities",
            "after")) {
            input.remove(key);
        }

        var random = new RecordingRandom(input.get("seed").getAsLong());
        var dimension = input.has("dimension") ? input.get("dimension").getAsString() : "minecraft:overworld";
        var time = input.has("time") ? input.get("time").getAsLong() : 0L;
        var level = ProbeLevel.allocate(random, attributes(registries, dimension, time), registries);

        var touched = new LinkedHashSet<BlockPos>();
        for (var placement : input.getAsJsonArray("setup")) {
            var value = placement.getAsJsonObject();
            var pos = position(value.getAsJsonArray("pos"));
            level.blocks.put(pos, state(value.get("state").getAsString()));
            touched.add(pos);
        }

        var origin = position(input.getAsJsonArray("origin"));
        touched.add(origin);

        if (input.has("place")) {
            // As /setblock places it: with every update, which runs its onPlace.
            level.setBlock(origin, state(input.get("place").getAsString()), Block.UPDATE_ALL);
        } else {
            // The level only random-ticks a state whose isRandomlyTicking holds, so the others draw nothing.
            var state = level.getBlockState(origin);
            var ticks = state.isRandomlyTicking();
            input.addProperty("ticks", ticks);
            if (ticks) {
                state.randomTick(level, origin, random);
            }
        }

        input.add("draws", random.draws);
        input.add("writes", level.writes);
        input.add("scheduled", level.scheduled);
        input.add("sounds", level.sounds);
        input.add("levelEvents", level.levelEvents);
        input.add("gameEvents", level.gameEvents);
        input.add("entities", level.entities);

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

    /**
     * The environment attributes of a level of the dimension type, as ServerLevel's default layers build them, but
     * without biomes and weather: the type's own attributes, then each timeline of its timeline tag, in the tag's
     * order, on a clock whose total ticks are time.
     */
    private static EnvironmentAttributeSystem attributes(HolderLookup.Provider registries, String dimension, long time)
        throws Exception {
        var typeKey = ResourceKey.create(Registries.DIMENSION_TYPE, Identifier.parse(dimension));
        DimensionType type = registries.lookupOrThrow(Registries.DIMENSION_TYPE).getOrThrow(typeKey).value();

        var builder = EnvironmentAttributeSystem.builder().addConstantLayer(type.attributes());
        var tag = type.timelines().unwrapKey().orElseThrow().location();
        for (var timeline : timelines(registries, tag)) {
            builder.addTimelineLayer(timeline, clock -> new FixedClock(time));
        }

        return builder.build();
    }

    // A timeline tag's timelines in its file's order, its nested tags expanded where they're listed, as the server
    // reads vanilla's data pack (data/<namespace>/tags/timeline/<path>.json).
    private static List<Holder<Timeline>> timelines(HolderLookup.Provider registries, Identifier tag) throws Exception {
        var resource = "/data/" + tag.getNamespace() + "/tags/timeline/" + tag.getPath() + ".json";
        var timelines = new ArrayList<Holder<Timeline>>();
        try (var stream = HatchingFixtures.class.getResourceAsStream(resource)) {
            if (stream == null) {
                throw new IllegalStateException("No timeline tag " + resource);
            }

            var file = JsonParser.parseReader(new InputStreamReader(stream, StandardCharsets.UTF_8)).getAsJsonObject();
            for (var value : file.getAsJsonArray("values")) {
                var name = value.getAsString();
                if (name.startsWith("#")) {
                    timelines.addAll(timelines(registries, Identifier.parse(name.substring(1))));
                    continue;
                }

                var key = ResourceKey.create(Registries.TIMELINE, Identifier.parse(name));
                timelines.add(registries.lookupOrThrow(Registries.TIMELINE).getOrThrow(key));
            }
        }

        return timelines;
    }

    private record FixedClock(long totalTicks) implements ClockInstance {
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

    private static JsonArray vector(double x, double y, double z) {
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
                draw.addProperty("value", (Number) value);
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

    /**
     * The block view: blocks from the case (air elsewhere), the level random being the tick's random, as ServerLevel
     * passes its own random to randomTick, and the case's environment attributes. Sounds, level events, game events
     * and added entities are recorded rather than sent.
     */
    private static final class ProbeLevel extends ServerLevel {
        Map<BlockPos, BlockState> blocks;
        JsonArray writes;
        JsonArray scheduled;
        JsonArray sounds;
        JsonArray levelEvents;
        JsonArray gameEvents;
        JsonArray entities;
        RandomSource tickRandom;
        EnvironmentAttributeSystem attributes;
        HolderLookup.Provider lookup;
        MinecraftServer owner;

        private ProbeLevel() {
            super(null, null, null, null, null, null, false, 0, List.of(), false);
            throw new UnsupportedOperationException();
        }

        static ProbeLevel allocate(RandomSource random, EnvironmentAttributeSystem attributes,
            HolderLookup.Provider lookup) throws Exception {
            var level = allocateInstance(ProbeLevel.class);
            level.blocks = new LinkedHashMap<>();
            level.writes = new JsonArray();
            level.scheduled = new JsonArray();
            level.sounds = new JsonArray();
            level.levelEvents = new JsonArray();
            level.gameEvents = new JsonArray();
            level.entities = new JsonArray();
            level.tickRandom = random;
            level.attributes = attributes;
            level.lookup = lookup;

            // Mobs' path navigation asks the server's debug subscribers whether to record paths; it has none.
            level.owner = allocateInstance(DedicatedServer.class);
            var subscribers = MinecraftServer.class.getDeclaredField("debugSubscribers");
            subscribers.setAccessible(true);
            subscribers.set(level.owner, new ServerDebugSubscribers(level.owner));
            return level;
        }

        private static <T> T allocateInstance(Class<T> type) throws Exception {
            var field = Class.forName("sun.misc.Unsafe").getDeclaredField("theUnsafe");
            field.setAccessible(true);
            var unsafe = field.get(null);
            return type.cast(unsafe.getClass().getMethod("allocateInstance", Class.class).invoke(unsafe, type));
        }

        @Override public MinecraftServer getServer() {
            return owner;
        }

        @Override public RandomSource getRandom() {
            return tickRandom;
        }

        @Override public EnvironmentAttributeSystem environmentAttributes() {
            return attributes;
        }

        @Override public RegistryAccess registryAccess() {
            return RegistryAccess.fromRegistryOfRegistries(BuiltInRegistries.REGISTRY);
        }

        @Override public FeatureFlagSet enabledFeatures() {
            return FeatureFlags.DEFAULT_FLAGS;
        }

        // Entities take their ids from the level's chunk source, which this view has none of.
        @Override public int getNextEntityId() {
            return entities.size() + 1;
        }

        @Override public boolean isClientSide() {
            return false;
        }

        @Override public int getMinY() {
            return -64;
        }

        @Override public int getHeight() {
            return 384;
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

        // Level.setBlock as far as a block's tick sees it: outside the build height nothing changes; otherwise the
        // block is stored, the new state's onPlace runs as LevelChunk.setBlockState runs it (on every change, unless
        // UPDATE_SKIP_ON_PLACE is set) and, unless UPDATE_KNOWN_SHAPE is set, the neighbours' shapes update.
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
                state.onPlace(this, pos, old, (flags & Block.UPDATE_MOVE_BY_PISTON) != 0);
            }

            if ((flags & Block.UPDATE_KNOWN_SHAPE) == 0 && limit > 0) {
                var neighbourFlags = flags & ~(Block.UPDATE_NEIGHBORS | Block.UPDATE_SUPPRESS_DROPS);
                old.updateIndirectNeighbourShapes(this, pos, neighbourFlags, limit - 1);
                state.updateNeighbourShapes(this, pos, neighbourFlags, limit - 1);
                state.updateIndirectNeighbourShapes(this, pos, neighbourFlags, limit - 1);
            }

            return true;
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

        @Override public void playSound(Entity except, double x, double y, double z, SoundEvent sound,
            SoundSource source, float volume, float pitch) {
            var played = new JsonObject();
            played.addProperty("sound", BuiltInRegistries.SOUND_EVENT.getKey(sound).toString());
            played.add("pos", vector(x, y, z));
            played.addProperty("source", source.getName());
            played.addProperty("volume", volume);
            played.addProperty("pitch", pitch);
            sounds.add(played);
        }

        @Override public void levelEvent(Entity source, int type, BlockPos pos, int data) {
            var event = new JsonObject();
            event.addProperty("type", type);
            event.add("pos", coordinates(pos));
            event.addProperty("data", data);
            levelEvents.add(event);
        }

        @Override public void gameEvent(Holder<GameEvent> gameEvent, Vec3 position, GameEvent.Context context) {
            var event = new JsonObject();
            event.addProperty("event", gameEvent.unwrapKey().orElseThrow().identifier().toString());
            event.add("pos", vector(position.x, position.y, position.z));
            gameEvents.add(event);
        }

        @Override public boolean addFreshEntity(Entity entity) {
            var added = new JsonObject();
            added.addProperty("type", BuiltInRegistries.ENTITY_TYPE.getKey(entity.getType()).toString());
            added.add("pos", vector(entity.getX(), entity.getY(), entity.getZ()));
            added.addProperty("yaw", entity.getYRot());
            if (entity instanceof AgeableMob ageable) {
                added.addProperty("age", ageable.getAge());
            }

            if (entity instanceof Turtle turtle) {
                added.add("home", coordinates(turtle.getHomePos()));
            }

            if (entity instanceof Mob mob) {
                added.addProperty("persistent", mob.isPersistenceRequired());
            }

            entities.add(added);
            return true;
        }
    }
}
