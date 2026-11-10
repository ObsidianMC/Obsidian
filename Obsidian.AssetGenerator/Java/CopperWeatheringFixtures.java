import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.resources.Identifier;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.block.BaseEntityBlock;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.entity.BlockEntity;
import net.minecraft.world.level.block.entity.ChestBlockEntity;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.Property;
import net.minecraft.world.level.chunk.LevelChunk;
import net.minecraft.world.level.chunk.LevelChunkSection;

/** Runs native random ticks and immediate shape propagation in a bounded block view, without a server clock. */
public final class CopperWeatheringFixtures {
    public static void main(String[] args) throws Exception {
        var root = JsonParser.parseString(Files.readString(Path.of(args[0]))).getAsJsonObject();
        root.addProperty("version", VanillaDumper.bootstrap());
        for (var value : root.getAsJsonArray("cases")) {
            capture(value.getAsJsonObject());
        }

        Files.writeString(Path.of(args[0]), new GsonBuilder().setPrettyPrinting().create().toJson(root) + "\n");
    }

    private static void capture(JsonObject input) throws Exception {
        var level = (ProbeLevel)allocate(ProbeLevel.class);
        level.blocks = new HashMap<>();
        level.entities = new HashMap<>();
        level.writes = new JsonArray();
        level.chunk = (ProbeChunk)allocate(ProbeChunk.class);
        level.chunk.level = level;
        for (var value : input.getAsJsonArray("setup")) {
            var placement = value.getAsJsonObject();
            var pos = position(placement.getAsJsonArray("pos"));
            var state = state(placement.get("state").getAsString());
            level.blocks.put(pos, state);
            if (state.getBlock() instanceof BaseEntityBlock block
                && !(input.has("missingEntity") && input.get("missingEntity").getAsBoolean())) {
                var entity = block.newBlockEntity(pos, state);
                entity.setLevel(level);
                level.entities.put(pos, entity);
            }
        }

        var origin = position(input.getAsJsonArray("origin"));
        if (input.has("open") && input.get("open").getAsBoolean()) {
            var original = (ChestBlockEntity)level.entities.get(origin);
            level.entities.put(origin, new ChestBlockEntity(origin, original.getBlockState()) {
                @Override public List<net.minecraft.world.entity.ContainerUser> getEntitiesWithContainerOpen() {
                    return java.util.Collections.singletonList(null);
                }
            });
        }

        var before = new HashMap<>(level.entities);
        var draws = new JsonArray();
        var seeded = RandomSource.create(input.get("seed").getAsLong());
        var random = (RandomSource)java.lang.reflect.Proxy.newProxyInstance(
            CopperWeatheringFixtures.class.getClassLoader(), new Class<?>[] { RandomSource.class },
            (proxy, method, arguments) -> {
                var result = method.invoke(seeded, arguments);
                var draw = new JsonObject();
                draw.addProperty("method", method.getName());
                draw.addProperty("value", (Number)result);
                draws.add(draw);
                return result;
            });
        var current = level.getBlockState(origin);
        input.addProperty("randomlyTicking", current.isRandomlyTicking());
        input.addProperty("owner", current.getBlock().getClass().getSimpleName());
        if (current.isRandomlyTicking()) {
            current.randomTick(level, origin, random);
        }

        input.add("draws", draws);
        input.addProperty("nextRandom", seeded.nextInt());
        input.add("writes", level.writes);
        var results = new JsonArray();
        for (var value : input.getAsJsonArray("setup")) {
            var placement = value.getAsJsonObject();
            var pos = position(placement.getAsJsonArray("pos"));
            var result = describe(pos, level.getBlockState(pos));
            if (before.containsKey(pos)) {
                var entity = level.entities.get(pos);
                result.addProperty("blockEntityType", BuiltInRegistries.BLOCK_ENTITY_TYPE
                    .getKey(entity.getType()).toString());
                result.addProperty("acceptsState", entity.getType().isValid(level.getBlockState(pos)));
                result.addProperty("sameBlockEntity", before.get(pos) == level.entities.get(pos));
            }

            results.add(result);
        }

        input.add("result", results);
    }

    private static final class ProbeLevel extends ServerLevel {
        Map<BlockPos, BlockState> blocks;
        Map<BlockPos, BlockEntity> entities;
        JsonArray writes;
        ProbeChunk chunk;

        private ProbeLevel() {
            super(null, null, null, null, null, null, false, 0, List.of(), false);
        }

        @Override public int getMinY() {
            return -64;
        }

        @Override public int getHeight() {
            return 384;
        }

        @Override public BlockState getBlockState(BlockPos pos) {
            return blocks.getOrDefault(pos, Blocks.AIR.defaultBlockState());
        }

        @Override public BlockEntity getBlockEntity(BlockPos pos) {
            return entities.get(pos);
        }

        @Override public List<net.minecraft.world.entity.Entity> getEntities(
            net.minecraft.world.entity.Entity except,
            net.minecraft.world.phys.AABB bounds,
            java.util.function.Predicate<? super net.minecraft.world.entity.Entity> predicate) {
            return List.of();
        }

        @Override public LevelChunk getChunk(int x, int z) {
            return chunk;
        }

        @Override public void scheduleTick(BlockPos pos, net.minecraft.world.level.material.Fluid fluid, int delay) {
        }

        @Override public boolean setBlock(BlockPos pos, BlockState state, int flags, int limit) {
            var previous = blocks.put(pos.immutable(), state);
            if (state.equals(previous)) {
                return false;
            }

            var entity = entities.get(pos);
            if (entity != null) {
                // Supply the bounded view's storage contract using vanilla's own valid-state predicate.
                // Chunk lifecycle side effects are outside this random-tick probe.
                if (entity.getType().isValid(state)) {
                    entity.setBlockState(state);
                } else {
                    entities.remove(pos);
                }
            }

            writes.add(describe(pos, state));
            if (limit > 0) {
                // Native shape routines synchronize connected doors and copper chests.
                for (var side : Direction.values()) {
                    var neighbor = pos.relative(side);
                    var old = getBlockState(neighbor);
                    if (old.isAir()) {
                        continue;
                    }

                    var updated = old.updateShape(this, this, neighbor, side.getOpposite(), pos,
                        state, RandomSource.create(0));
                    if (!updated.equals(old)) {
                        setBlock(neighbor, updated, flags, limit - 1);
                    }
                }
            }

            return true;
        }
    }

    // Supply only the section reads the native scan's cache needs; it still performs its own filtering and order.
    private static final class ProbeChunk extends LevelChunk {
        ProbeLevel level;

        private ProbeChunk() {
            super(null, null);
        }

        @Override public int getSectionIndexFromSectionY(int sectionY) {
            return sectionY;
        }

        @Override public LevelChunkSection getSection(int index) {
            try {
                var section = (ProbeSection)allocate(ProbeSection.class);
                section.level = level;
                section.y = index * 16;
                return section;
            } catch (Exception exception) {
                throw new RuntimeException(exception);
            }
        }
    }

    private static final class ProbeSection extends LevelChunkSection {
        ProbeLevel level;
        int y;

        private ProbeSection() {
            super(null, null);
        }

        @Override public boolean maybeHas(java.util.function.Predicate<BlockState> predicate) {
            return true;
        }

        @Override public BlockState getBlockState(int x, int localY, int z) {
            return level.getBlockState(new BlockPos(x, y + localY, z));
        }
    }

    private static Object allocate(Class<?> type) throws Exception {
        var field = Class.forName("sun.misc.Unsafe").getDeclaredField("theUnsafe");
        field.setAccessible(true);
        var unsafe = field.get(null);
        return unsafe.getClass().getMethod("allocateInstance", Class.class).invoke(unsafe, type);
    }

    private static JsonObject describe(BlockPos pos, BlockState state) {
        var result = new JsonObject();
        var coordinates = new JsonArray();
        coordinates.add(pos.getX());
        coordinates.add(pos.getY());
        coordinates.add(pos.getZ());
        result.add("pos", coordinates);
        result.addProperty("state", state.toString().replace("Block{", "").replace("}", ""));
        return result;
    }

    private static BlockPos position(JsonArray values) {
        return new BlockPos(values.get(0).getAsInt(), values.get(1).getAsInt(), values.get(2).getAsInt());
    }

    private static BlockState state(String text) {
        int bracket = text.indexOf('[');
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
}
