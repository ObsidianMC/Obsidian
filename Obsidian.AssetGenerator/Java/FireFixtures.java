import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.HolderLookup;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.sounds.SoundEvent;
import net.minecraft.sounds.SoundSource;
import net.minecraft.util.RandomSource;
import net.minecraft.world.Difficulty;
import net.minecraft.world.attribute.EnvironmentAttributeSystem;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.flag.FeatureFlags;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.biome.Biome;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.EntityBlock;
import net.minecraft.world.level.block.entity.BlockEntity;
import net.minecraft.world.level.block.entity.LecternBlockEntity;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.Property;
import net.minecraft.world.level.dimension.DimensionType;
import net.minecraft.world.level.gameevent.GameEvent;
import net.minecraft.world.level.gamerules.GameRules;
import net.minecraft.world.level.levelgen.Heightmap;
import net.minecraft.world.level.levelgen.LegacyRandomSource;
import net.minecraft.world.level.levelgen.PositionalRandomFactory;
import net.minecraft.world.level.material.FluidState;
import net.minecraft.world.level.redstone.NeighborUpdater;
import net.minecraft.world.phys.Vec3;

/**
 * Runs vanilla's fire on a bounded block view: a fire block's scheduled tick ({@code FireBlock.tick}), or a block
 * placed with {@code Level.setBlock} so that its {@code onPlace} runs. Each case lists blocks around an origin, the
 * weather, difficulty, dimension and biome, and the seeds to run it with; each seed's run records every draw from
 * the level's random, the final state of every block it touched, the block ticks it scheduled (the first per
 * position and block, as vanilla ignores the rest) and the entities it added.
 */
public final class FireFixtures {
    private static final BlockPos ORIGIN = new BlockPos(0, 100, 0);

    public static void main(String[] arguments) throws Exception {
        var version = VanillaDumper.bootstrap();
        var registries = (HolderLookup.Provider)VanillaDumper.vanillaRegistries();
        var root = JsonParser.parseString(Files.readString(Path.of(arguments[0]))).getAsJsonObject();
        root.addProperty("version", version);

        for (var entry : root.getAsJsonArray("cases")) {
            var input = entry.getAsJsonObject();
            var runs = new JsonArray();
            for (var seed : input.getAsJsonArray("seeds")) {
                runs.add(run(input, seed.getAsLong(), registries));
            }

            input.add("runs", runs);
        }

        Files.writeString(Path.of(arguments[0]),
            new GsonBuilder().setPrettyPrinting().disableHtmlEscaping().create().toJson(root) + "\n");
    }

    private static JsonObject run(JsonObject input, long seed, HolderLookup.Provider registries) throws Exception {
        var level = ProbeLevel.create(input, seed, registries);
        for (var placement : input.getAsJsonArray("setup")) {
            var value = placement.getAsJsonObject();
            level.put(ORIGIN.offset(offset(value.getAsJsonArray("at"))), state(value.get("state").getAsString()));
        }

        var touched = new HashSet<BlockPos>(level.blocks.keySet());
        touched.add(ORIGIN);

        var kind = input.get("kind").getAsString();
        if (kind.equals("tick")) {
            var state = level.getBlockState(ORIGIN);
            state.tick(level, ORIGIN, level.recorder());
        } else if (kind.equals("place")) {
            level.setBlock(ORIGIN, state(input.get("place").getAsString()), Block.UPDATE_ALL);
        } else {
            throw new IllegalArgumentException("Unknown kind " + kind);
        }

        touched.addAll(level.written);

        var result = new JsonObject();
        result.addProperty("seed", seed);
        result.add("draws", level.recorder().draws);
        result.addProperty("nextRandom", level.recorder().inner.nextInt());
        result.add("after", after(level, touched));
        result.add("ticks", level.ticks);
        result.add("entities", level.entities);
        return result;
    }

    // The final state of every touched block, in x, y, z order, relative to the origin.
    private static JsonArray after(ProbeLevel level, Set<BlockPos> touched) {
        var sorted = new ArrayList<>(touched);
        sorted.sort((left, right) -> {
            if (left.getX() != right.getX()) {
                return Integer.compare(left.getX(), right.getX());
            }

            if (left.getY() != right.getY()) {
                return Integer.compare(left.getY(), right.getY());
            }

            return Integer.compare(left.getZ(), right.getZ());
        });

        var result = new JsonArray();
        for (var position : sorted) {
            var block = new JsonObject();
            block.add("at", relative(position));
            block.addProperty("state", text(level.getBlockState(position)));
            result.add(block);
        }

        return result;
    }

    private static BlockPos offset(JsonArray values) {
        return new BlockPos(values.get(0).getAsInt(), values.get(1).getAsInt(), values.get(2).getAsInt());
    }

    private static JsonArray relative(BlockPos position) {
        var result = new JsonArray();
        result.add(position.getX() - ORIGIN.getX());
        result.add(position.getY() - ORIGIN.getY());
        result.add(position.getZ() - ORIGIN.getZ());
        return result;
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
                var property = block.getStateDefinition().getProperty(parts[0]);
                state = withProperty(state, property, parts[1]);
            }
        }

        return state;
    }

    private static <T extends Comparable<T>> BlockState withProperty(BlockState state, Property<T> property,
        String value) {
        return state.setValue(property, property.getValue(value).orElseThrow());
    }

    /** The level's random: a legacy random with the run's seed that records each draw it gives. */
    private static final class RecordingRandom implements RandomSource {
        final LegacyRandomSource inner;
        final JsonArray draws = new JsonArray();

        RecordingRandom(long seed) {
            inner = new LegacyRandomSource(seed);
        }

        @Override public RandomSource fork() {
            draws.add("fork");
            return inner.fork();
        }

        @Override public PositionalRandomFactory forkPositional() {
            draws.add("forkPositional");
            return inner.forkPositional();
        }

        @Override public void setSeed(long seed) {
            inner.setSeed(seed);
        }

        @Override public int nextInt() {
            var value = inner.nextInt();
            draws.add("int=" + value);
            return value;
        }

        @Override public int nextInt(int bound) {
            var value = inner.nextInt(bound);
            draws.add("int(" + bound + ")=" + value);
            return value;
        }

        @Override public long nextLong() {
            draws.add("long");
            return inner.nextLong();
        }

        @Override public boolean nextBoolean() {
            draws.add("boolean");
            return inner.nextBoolean();
        }

        @Override public float nextFloat() {
            draws.add("float");
            return inner.nextFloat();
        }

        @Override public double nextDouble() {
            draws.add("double");
            return inner.nextDouble();
        }

        @Override public double nextGaussian() {
            draws.add("gaussian");
            return inner.nextGaussian();
        }
    }

    /**
     * A server level holding only the case's blocks. Setting a block runs what {@code LevelChunk.setBlockState} and
     * {@code Level.setBlock} run on a server: the old block entity's removal side effects, the old block's removal,
     * the new block's {@code onPlace}, then neighbour and shape updates, as the flags ask.
     */
    private static final class ProbeLevel extends ServerLevel {
        Map<BlockPos, BlockState> blocks;
        Map<BlockPos, BlockEntity> blockEntities;
        Set<BlockPos> written;
        Set<String> scheduled;
        JsonArray ticks;
        JsonArray entities;
        RecordingRandom recording;
        GameRules rules;
        DimensionType dimension;
        Biome biome;
        Difficulty difficulty;
        boolean raining;
        EnvironmentAttributeSystem environment;

        private ProbeLevel() {
            super(null, null, null, null, null, null, false, 0, List.of(), false);
            throw new UnsupportedOperationException();
        }

        static ProbeLevel create(JsonObject input, long seed, HolderLookup.Provider registries) throws Exception {
            var level = (ProbeLevel)allocate(ProbeLevel.class);
            level.blocks = new HashMap<>();
            level.blockEntities = new HashMap<>();
            level.written = new HashSet<>();
            level.scheduled = new HashSet<>();
            level.ticks = new JsonArray();
            level.entities = new JsonArray();
            level.recording = new RecordingRandom(seed);
            level.rules = new GameRules(FeatureFlags.DEFAULT_FLAGS);
            level.raining = input.has("raining") && input.get("raining").getAsBoolean();
            level.difficulty = Difficulty.byName(stringOr(input, "difficulty", "normal"));

            var dimension = ResourceKey.create(Registries.DIMENSION_TYPE,
                Identifier.parse(stringOr(input, "dimension", "minecraft:overworld")));
            level.dimension = registries.lookupOrThrow(Registries.DIMENSION_TYPE).getOrThrow(dimension).value();
            var biome = ResourceKey.create(Registries.BIOME,
                Identifier.parse(stringOr(input, "biome", "minecraft:plains")));
            level.biome = registries.lookupOrThrow(Registries.BIOME).getOrThrow(biome).value();

            // The case has one biome, so its attributes apply everywhere, over the dimension type's.
            level.environment = EnvironmentAttributeSystem.builder()
                .addConstantLayer(level.dimension.attributes())
                .addConstantLayer(level.biome.getAttributes())
                .build();

            // Entities read the level's random through the field, as primed TNT does.
            var random = Level.class.getDeclaredField("random");
            random.setAccessible(true);
            random.set(level, level.recording);
            return level;
        }

        RecordingRandom recorder() {
            return recording;
        }

        // A setup block, without updates; a lectern holding a book gets its block entity with a book.
        void put(BlockPos position, BlockState state) {
            blocks.put(position, state);
            if (state.getBlock() instanceof EntityBlock entityBlock) {
                // The book first: the lectern marks itself changed in its level, which this one has no chunks for.
                var blockEntity = entityBlock.newBlockEntity(position, state);
                if (blockEntity instanceof LecternBlockEntity lectern
                    && state.getValue(net.minecraft.world.level.block.LecternBlock.HAS_BOOK)) {
                    lectern.setBook(new ItemStack(Items.WRITABLE_BOOK));
                }

                blockEntity.setLevel(this);

                blockEntities.put(position, blockEntity);
            }
        }

        @Override public RandomSource getRandom() {
            return recording;
        }

        @Override public GameRules getGameRules() {
            return rules;
        }

        @Override public DimensionType dimensionType() {
            return dimension;
        }

        @Override public EnvironmentAttributeSystem environmentAttributes() {
            return environment;
        }

        @Override public Difficulty getDifficulty() {
            return difficulty;
        }

        @Override public boolean canSpreadFireAround(BlockPos pos) {
            return true;
        }

        @Override public boolean isRaining() {
            return raining;
        }

        // Rain reaches a block when nothing that blocks motion is above it among the case's blocks.
        @Override public boolean isRainingAt(BlockPos pos) {
            if (!raining) {
                return false;
            }

            for (var entry : blocks.entrySet()) {
                var above = entry.getKey();
                var column = above.getX() == pos.getX() && above.getZ() == pos.getZ();
                var blocksRain = Heightmap.Types.MOTION_BLOCKING.isOpaque().test(entry.getValue());
                if (column && above.getY() > pos.getY() && blocksRain) {
                    return false;
                }
            }

            return biome.getPrecipitationAt(pos, 63) == Biome.Precipitation.RAIN;
        }

        @Override public BlockState getBlockState(BlockPos pos) {
            return blocks.getOrDefault(pos, Blocks.AIR.defaultBlockState());
        }

        @Override public FluidState getFluidState(BlockPos pos) {
            return getBlockState(pos).getFluidState();
        }

        @Override public BlockEntity getBlockEntity(BlockPos pos) {
            return blockEntities.get(pos);
        }

        @Override public int getMinY() {
            return -64;
        }

        @Override public int getHeight() {
            return 384;
        }

        @Override public long getGameTime() {
            return 20;
        }

        @Override public int getNextEntityId() {
            return 1;
        }

        @Override public boolean setBlock(BlockPos pos, BlockState state, int flags, int limit) {
            var position = pos.immutable();
            var old = getBlockState(position);
            if (old == state) {
                return false;
            }

            blocks.put(position, state);
            written.add(position);

            // LevelChunk.setBlockState: the replaced block entity's side effects, the removal, then onPlace.
            var changed = !old.is(state.getBlock());
            if (changed && old.hasBlockEntity() && !state.shouldChangedStateKeepBlockEntity(old)) {
                var blockEntity = blockEntities.remove(position);
                if (blockEntity != null && (flags & Block.UPDATE_SKIP_BLOCK_ENTITY_SIDEEFFECTS) == 0) {
                    blockEntity.preRemoveSideEffects(position, old);
                }
            }

            if (changed && (flags & Block.UPDATE_NEIGHBORS) != 0) {
                old.affectNeighborsAfterRemoval(this, position, false);
            }

            if (!getBlockState(position).is(state.getBlock())) {
                return false;
            }

            if ((flags & Block.UPDATE_SKIP_ON_PLACE) == 0) {
                state.onPlace(this, position, old, false);
            }

            // Level.setBlock: neighbour updates, then shape updates, if the block is still the one set.
            if (getBlockState(position) != state) {
                return true;
            }

            if ((flags & Block.UPDATE_NEIGHBORS) != 0) {
                updateNeighborsAt(position, old.getBlock());
            }

            if ((flags & Block.UPDATE_KNOWN_SHAPE) == 0 && limit > 0) {
                var neighbourFlags = flags & ~(Block.UPDATE_NEIGHBORS | Block.UPDATE_SUPPRESS_DROPS);
                old.updateIndirectNeighbourShapes(this, position, neighbourFlags, limit - 1);
                state.updateNeighbourShapes(this, position, neighbourFlags, limit - 1);
                state.updateIndirectNeighbourShapes(this, position, neighbourFlags, limit - 1);
            }

            return true;
        }

        // Level.destroyBlock as shape updates call it: the cases only destroy fire, which drops nothing.
        @Override public boolean destroyBlock(BlockPos pos, boolean drops, Entity breaker, int limit) {
            var state = getBlockState(pos);
            if (state.isAir()) {
                return false;
            }

            if (!(state.getBlock() instanceof net.minecraft.world.level.block.BaseFireBlock)) {
                throw new IllegalStateException("A case destroyed " + state + ", whose drops aren't recorded");
            }

            return setBlock(pos, getFluidState(pos).createLegacyBlock(), Block.UPDATE_ALL, limit);
        }

        @Override public void levelEvent(Entity source, int event, BlockPos pos, int data) {
        }

        @Override public void updateNeighborsAt(BlockPos pos, Block source) {
            for (var direction : NeighborUpdater.UPDATE_ORDER) {
                var neighbour = pos.relative(direction);
                NeighborUpdater.executeUpdate(this, getBlockState(neighbour), neighbour, source, null, false);
            }
        }

        @Override public void neighborShapeChanged(Direction direction, BlockPos pos, BlockPos neighbourPos,
            BlockState neighbourState, int flags, int limit) {
            NeighborUpdater.executeShapeUpdate(this, direction, pos, neighbourPos, neighbourState, flags, limit);
        }

        // Vanilla's level ticks keep one tick per position and block, and ignore scheduling it again.
        @Override public void scheduleTick(BlockPos pos, Block block, int delay) {
            var id = BuiltInRegistries.BLOCK.getKey(block).toString();
            if (!scheduled.add(pos.asLong() + "/" + id)) {
                return;
            }

            var tick = new JsonObject();
            tick.add("at", relative(pos));
            tick.addProperty("block", id);
            tick.addProperty("delay", delay);
            ticks.add(tick);
        }

        @Override public boolean addFreshEntity(Entity entity) {
            var recorded = new JsonObject();
            recorded.addProperty("type", BuiltInRegistries.ENTITY_TYPE.getKey(entity.getType()).toString());
            var position = new JsonArray();
            position.add(entity.getX() - ORIGIN.getX());
            position.add(entity.getY() - ORIGIN.getY());
            position.add(entity.getZ() - ORIGIN.getZ());
            recorded.add("pos", position);
            if (entity instanceof ItemEntity item) {
                recorded.addProperty("item", BuiltInRegistries.ITEM.getKey(item.getItem().getItem()).toString());
                recorded.addProperty("count", item.getItem().getCount());
            }

            entities.add(recorded);
            return true;
        }

        @Override public void playSound(Entity except, double x, double y, double z, SoundEvent sound,
            SoundSource source, float volume, float pitch) {
        }

        @Override public void gameEvent(net.minecraft.core.Holder<GameEvent> event, Vec3 position,
            GameEvent.Context context) {
        }
    }

    private static String stringOr(JsonObject input, String key, String fallback) {
        return input.has(key) ? input.get(key).getAsString() : fallback;
    }

    private static Object allocate(Class<?> type) throws Exception {
        var field = Class.forName("sun.misc.Unsafe").getDeclaredField("theUnsafe");
        field.setAccessible(true);
        var unsafe = field.get(null);
        return unsafe.getClass().getMethod("allocateInstance", Class.class).invoke(unsafe, type);
    }
}
