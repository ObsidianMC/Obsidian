import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import com.mojang.serialization.Lifecycle;
import com.mojang.serialization.MapCodec;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Comparator;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.function.BiConsumer;
import java.util.function.Predicate;
import java.util.stream.Stream;
import java.util.zip.ZipFile;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.Holder;
import net.minecraft.core.MappedRegistry;
import net.minecraft.core.Registry;
import net.minecraft.core.RegistryAccess;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.level.ServerChunkCache;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.util.RandomSource;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.level.WorldGenLevel;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.BonemealSource;
import net.minecraft.world.level.block.BonemealableBlock;
import net.minecraft.world.level.blockscan.BlockMatcher;
import net.minecraft.world.level.blockscan.BlockStateConsumer;
import net.minecraft.world.level.blockscan.BoxBlockMatcher;
import net.minecraft.util.Continuation;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.chunk.ChunkGenerator;
import net.minecraft.world.level.levelgen.blockpredicates.BlockPredicate;
import net.minecraft.world.level.levelgen.feature.AbstractHugeMushroomFeature;
import net.minecraft.world.level.levelgen.feature.Feature;
import net.minecraft.world.level.levelgen.feature.stateproviders.BlockStateProvider;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.Property;
import net.minecraft.world.level.gamerules.GameRules;
import net.minecraft.world.level.levelgen.PositionalRandomFactory;
import net.minecraft.world.level.material.Fluid;
import net.minecraft.world.level.material.FluidState;
import net.minecraft.world.level.redstone.NeighborUpdater;
import net.minecraft.world.ticks.TickPriority;

/**
 * Calls vanilla's randomTick on one block state against a bounded, explicitly supplied block view, with a seeded random
 * that records every draw. Writes run vanilla's neighbour shape updates (Level.setBlock without UPDATE_KNOWN_SHAPE), so
 * a growing plant's old head turning into its body is part of the result; neighbour notifications are not run.
 *
 * Usage: GrowingPlantTickFixtures <fixture.json>. Each case's setup, origin, actions, seed, light and rules are its
 * input; ticks, draws, writes, scheduled and after are replaced.
 */
public final class GrowingPlantTickFixtures {
    private static final String NOTES = "Vanilla randomTick of the origin's state, recorded by the asset generator's "
        + "GrowingPlantTickFixtures.java against a block view of the case's setup (air elsewhere, void air outside the "
        + "build height of -64 to 319), with every raw brightness the case's light (15 by default), the spread_vines "
        + "rule as spreadVines says (true by default), and a legacy random seeded with seed as both the tick's random "
        + "and the level's. ticks is the state's isRandomlyTicking; a state that doesn't tick isn't ticked, and a case "
        + "without an origin ticks nothing. After the tick, each of the case's actions sets its state with "
        + "Level.setBlock and UPDATE_ALL, or without a state removes the block (Level.removeBlock). draws "
        + "lists each draw in order with its bound and result, the shape updates' included. writes are the block "
        + "changes in order with their "
        + "update flags, including those of the neighbour shape updates they cause (neighbour notifications are not "
        + "run). scheduled lists the block and fluid ticks scheduled. after holds the final state of every setup or "
        + "written position. A case with bonemeal true uses bone meal on the origin instead of ticking it, as "
        + "BoneMealItem.growCrop does (isValidBonemealTarget, recorded as valid, then isBonemealSuccess and "
        + "performBonemeal with the level's random). The level's feature registry holds a stand-in for every feature "
        + "in the jar's data/minecraft/worldgen/feature (a huge mushroom's with its foliage_radius, which bone meal "
        + "reads; no tree's is a TreeFeature, so a sapling's minimum height reads 0): placing one draws nothing, "
        + "returns the case's features entry for it (true by default) and is recorded in placed with its origin.";

    public static void main(String[] arguments) throws Exception {
        var version = VanillaDumper.bootstrap();
        var path = Path.of(arguments[0]);
        var root = JsonParser.parseString(Files.readString(path)).getAsJsonObject();
        root.addProperty("_notes", NOTES);
        root.addProperty("version", version);

        for (var entry : root.getAsJsonArray("cases")) {
            capture(entry.getAsJsonObject());
        }

        var gson = new GsonBuilder().serializeNulls().setPrettyPrinting().disableHtmlEscaping().create();
        Files.writeString(path, gson.toJson(root) + "\n");
    }

    private static void capture(JsonObject input) throws Exception {
        for (var key : List.of("ticks", "valid", "draws", "writes", "scheduled", "placed", "after")) {
            input.remove(key);
        }

        var random = new RecordingRandom(input.get("seed").getAsLong());
        var level = ProbeLevel.allocate(random);
        if (input.has("features")) {
            for (var entry : input.getAsJsonObject("features").entrySet()) {
                level.featureResults.put(entry.getKey(), entry.getValue().getAsBoolean());
            }
        }

        if (input.has("light")) {
            level.light = input.get("light").getAsInt();
        }

        if (input.has("spreadVines")) {
            level.rules.set(GameRules.SPREAD_VINES, input.get("spreadVines").getAsBoolean(), null);
        }

        var touched = new LinkedHashSet<BlockPos>();
        for (var placement : input.getAsJsonArray("setup")) {
            var value = placement.getAsJsonObject();
            var pos = position(value.getAsJsonArray("pos"));
            level.blocks.put(pos, state(value.get("state").getAsString()));
            touched.add(pos);
        }

        var bonemeal = input.has("bonemeal") && input.get("bonemeal").getAsBoolean();
        if (input.has("origin") && bonemeal) {
            input.addProperty("valid", useBonemeal(level, position(input.getAsJsonArray("origin"))));
        } else if (input.has("origin")) {
            var origin = position(input.getAsJsonArray("origin"));
            var state = level.getBlockState(origin);

            // The level only random-ticks a state whose isRandomlyTicking holds, so the others draw nothing.
            var ticks = state.isRandomlyTicking();
            input.addProperty("ticks", ticks);
            if (ticks) {
                state.randomTick(level, origin, random);
            }
        }

        // What a player or the world does next: a tip broken off, a support block placed.
        if (input.has("actions")) {
            for (var entry : input.getAsJsonArray("actions")) {
                var action = entry.getAsJsonObject();
                var pos = position(action.getAsJsonArray("pos"));
                if (action.has("state")) {
                    level.setBlock(pos, state(action.get("state").getAsString()), Block.UPDATE_ALL);
                } else {
                    level.removeBlock(pos, false);
                }

                touched.add(pos);
            }
        }

        input.add("draws", random.draws);
        input.add("writes", level.writes);
        input.add("scheduled", level.scheduled);

        // Only cases that place a feature record any, so the others keep their shape.
        if (!level.placed.isEmpty()) {
            input.add("placed", level.placed);
        }

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

    // BoneMealItem.growCrop as a player's use runs it on the server: a block that takes bone meal grows when its
    // chance comes up, both drawn from the level's random. Whether it took the bone meal is the result.
    private static boolean useBonemeal(ProbeLevel level, BlockPos origin) {
        var state = level.getBlockState(origin);
        if (!(state.getBlock() instanceof BonemealableBlock block)
            || !block.isValidBonemealTarget(level, origin, state, BonemealSource.INTERACTION)) {
            return false;
        }

        if (block.isBonemealSuccess(level, level.getRandom(), origin, state, BonemealSource.INTERACTION)) {
            block.performBonemeal(level, level.getRandom(), origin, state, BonemealSource.INTERACTION);
        }

        return true;
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

    /**
     * The block view: blocks from the case (air elsewhere), one raw brightness everywhere, and the level random being
     * the tick's random, as ServerLevel passes its own random to randomTick.
     */
    private static final class ProbeLevel extends ServerLevel {
        Map<BlockPos, BlockState> blocks;
        JsonArray writes;
        JsonArray scheduled;
        RandomSource tickRandom;
        GameRules rules;
        int light;
        JsonArray placed;
        Map<String, Boolean> featureResults;
        RegistryAccess registries;
        ProbeChunks chunks;

        private ProbeLevel() {
            super(null, null, null, null, null, null, false, 0, List.of(), false);
            throw new UnsupportedOperationException();
        }

        static ProbeLevel allocate(RandomSource random) throws Exception {
            var field = Class.forName("sun.misc.Unsafe").getDeclaredField("theUnsafe");
            field.setAccessible(true);
            var unsafe = field.get(null);
            var level = (ProbeLevel)unsafe.getClass().getMethod("allocateInstance", Class.class)
                .invoke(unsafe, ProbeLevel.class);
            level.blocks = new LinkedHashMap<>();
            level.writes = new JsonArray();
            level.scheduled = new JsonArray();
            level.tickRandom = random;
            level.rules = new GameRules(List.of(GameRules.SPREAD_VINES));
            level.light = 15;
            level.placed = new JsonArray();
            level.featureResults = new LinkedHashMap<>();
            level.registries = new ProbeRegistries(stubFeatures(level));
            level.chunks = (ProbeChunks)unsafe.getClass().getMethod("allocateInstance", Class.class)
                .invoke(unsafe, ProbeChunks.class);
            return level;
        }

        // A stand-in for every feature the jar defines, which records where it's placed and returns the case's result.
        private static Registry<Feature> stubFeatures(ProbeLevel level) throws Exception {
            var registry = new MappedRegistry<Feature>(Registries.FEATURE, Lifecycle.stable());
            var jar = Blocks.class.getProtectionDomain().getCodeSource().getLocation().toURI();
            var prefix = "data/minecraft/worldgen/feature/";
            try (var zip = new ZipFile(Path.of(jar).toFile())) {
                var entries = zip.entries();
                while (entries.hasMoreElements()) {
                    var name = entries.nextElement().getName();
                    if (!name.startsWith(prefix) || !name.endsWith(".json")) {
                        continue;
                    }

                    var id = "minecraft:" + name.substring(prefix.length(), name.length() - ".json".length());
                    JsonObject definition;
                    try (var stream = zip.getInputStream(zip.getEntry(name))) {
                        definition = JsonParser.parseString(new String(stream.readAllBytes())).getAsJsonObject();
                    }

                    var type = definition.get("type").getAsString();
                    Feature stub;
                    if (type.equals("minecraft:huge_brown_mushroom") || type.equals("minecraft:huge_red_mushroom")) {
                        var radius = definition.has("foliage_radius")
                            ? definition.get("foliage_radius").getAsInt()
                            : 2;
                        stub = new StubMushroom(level, id, radius);
                    } else {
                        stub = new StubFeature(level, id);
                    }

                    Registry.register(registry, ResourceKey.create(Registries.FEATURE, Identifier.parse(id)), stub);
                }
            }

            // A registry binds its holders' values when it's frozen.
            registry.freeze();
            return registry;
        }

        boolean place(String id, BlockPos origin) {
            var record = new JsonObject();
            record.addProperty("feature", id);
            record.add("pos", coordinates(origin));
            placed.add(record);
            return featureResults.getOrDefault(id, true);
        }

        @Override public ServerChunkCache getChunkSource() {
            return chunks;
        }

        @Override public RandomSource getRandom() {
            return tickRandom;
        }

        @Override public GameRules getGameRules() {
            return rules;
        }

        // Stems look their fruit and attached stem up by key; trees, mushrooms and the like their features.
        @Override public RegistryAccess registryAccess() {
            return registries;
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

        // Vanilla scans chunk sections, which this view has none of; the matches are the same block by block.
        @Override public BlockMatcher findBlocksIn(BlockPos from, BlockPos to) {
            return new BoxBlockMatcher(this, from, to) {
                @Override public boolean atLeastMatched(int n) {
                    var count = 0;
                    for (var pos : BlockPos.betweenClosed(from, to)) {
                        if (statePredicate.test(level.getBlockState(pos)) && ++count >= n) {
                            return true;
                        }
                    }

                    return false;
                }

                @Override protected boolean anyMatched(Predicate<BlockState> predicate) {
                    for (var pos : BlockPos.betweenClosed(from, to)) {
                        if (predicate.test(level.getBlockState(pos))) {
                            return true;
                        }
                    }

                    return false;
                }

                @Override public void forEach(BiConsumer<BlockPos, BlockState> consumer) {
                    forEachUntil((pos, state) -> {
                        consumer.accept(pos, state);
                        return Continuation.CONTINUE;
                    });
                }

                @Override public boolean forEachUntil(BlockStateConsumer consumer) {
                    for (var pos : BlockPos.betweenClosed(from, to)) {
                        var state = level.getBlockState(pos);
                        if (statePredicate.test(state) && consumer.apply(pos, state) == Continuation.ABORT) {
                            return true;
                        }
                    }

                    return false;
                }
            };
        }

        @Override public int getRawBrightness(BlockPos pos, int darkening) {
            return Math.max(0, light - darkening);
        }

        // Level.setBlock as far as a random tick sees it: outside the build height nothing changes; otherwise the
        // block is stored and, unless UPDATE_KNOWN_SHAPE is set, its neighbours' shapes update.
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

            if ((flags & Block.UPDATE_KNOWN_SHAPE) == 0 && limit > 0) {
                var neighbourFlags = flags & ~(Block.UPDATE_NEIGHBORS | Block.UPDATE_SUPPRESS_DROPS);
                old.updateIndirectNeighbourShapes(this, pos, neighbourFlags, limit - 1);
                state.updateNeighbourShapes(this, pos, neighbourFlags, limit - 1);
                state.updateIndirectNeighbourShapes(this, pos, neighbourFlags, limit - 1);
            }

            return true;
        }

        @Override public boolean destroyBlock(BlockPos pos, boolean drops, Entity breaker, int limit) {
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
    }

    /** The probe's chunk source, which only answers the generator features are placed with (none). */
    private static final class ProbeChunks extends ServerChunkCache {
        private ProbeChunks() {
            super(null, null, null, null, null, null, 0, 0, false, null);
            throw new UnsupportedOperationException();
        }

        @Override public ChunkGenerator getGenerator() {
            return null;
        }
    }

    /** The built-in registries, with the probe's stand-in features as the feature registry. */
    private static final class ProbeRegistries implements RegistryAccess {
        private final RegistryAccess builtIn = RegistryAccess.fromRegistryOfRegistries(BuiltInRegistries.REGISTRY);
        private final Registry<Feature> features;

        ProbeRegistries(Registry<Feature> features) {
            this.features = features;
        }

        @SuppressWarnings("unchecked")
        @Override public <E> Optional<Registry<E>> lookup(ResourceKey<? extends Registry<? extends E>> key) {
            if (key.equals(Registries.FEATURE)) {
                return Optional.of((Registry<E>)(Registry<?>)features);
            }

            return builtIn.lookup(key);
        }

        @Override public Stream<RegistryAccess.RegistryEntry<?>> registries() {
            return builtIn.registries();
        }
    }

    /** A feature that records where it's placed, and draws nothing. */
    private record StubFeature(ProbeLevel level, String id) implements Feature {
        @Override public MapCodec<? extends Feature> codec() {
            throw new UnsupportedOperationException("codec");
        }

        @Override public boolean place(WorldGenLevel target, ChunkGenerator generator, RandomSource random,
            BlockPos origin) {
            return level.place(id, origin);
        }
    }

    /** A huge mushroom's stand-in, whose foliage radius bone meal reads to check the room above. */
    private record StubMushroom(ProbeLevel level, String id, int foliageRadius)
        implements AbstractHugeMushroomFeature {
        @Override public Holder<BlockStateProvider> capProvider() {
            throw new UnsupportedOperationException("capProvider");
        }

        @Override public Holder<BlockStateProvider> stemProvider() {
            throw new UnsupportedOperationException("stemProvider");
        }

        @Override public BlockPredicate canPlaceOn() {
            throw new UnsupportedOperationException("canPlaceOn");
        }

        @Override public int getTreeRadiusForHeight(int unused, int treeHeight, int leafRadius, int yo) {
            throw new UnsupportedOperationException("getTreeRadiusForHeight");
        }

        @Override public void makeCap(WorldGenLevel target, RandomSource random, BlockPos origin, int treeHeight,
            BlockPos.MutableBlockPos blockPos) {
            throw new UnsupportedOperationException("makeCap");
        }

        @Override public MapCodec<? extends AbstractHugeMushroomFeature> codec() {
            throw new UnsupportedOperationException("codec");
        }

        @Override public boolean place(WorldGenLevel target, ChunkGenerator generator, RandomSource random,
            BlockPos origin) {
            return level.place(id, origin);
        }
    }
}
