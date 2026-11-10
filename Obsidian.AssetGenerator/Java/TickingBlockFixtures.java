import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import java.lang.reflect.Field;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Comparator;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.Holder;
import net.minecraft.core.HolderLookup;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.ResourceKey;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.resources.Identifier;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.util.RandomSource;
import net.minecraft.world.attribute.EnvironmentAttributeSystem;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EntityTypes;
import net.minecraft.world.entity.MoverType;
import net.minecraft.world.entity.item.FallingBlockEntity;
import net.minecraft.world.level.ItemLike;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.Property;
import net.minecraft.world.level.gameevent.GameEvent;
import net.minecraft.world.level.gamerules.GameRules;
import net.minecraft.world.level.levelgen.PositionalRandomFactory;
import net.minecraft.world.level.material.Fluid;
import net.minecraft.world.level.material.FluidState;
import net.minecraft.world.level.redstone.NeighborUpdater;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.Vec3;
import net.minecraft.world.ticks.LevelTicks;
import net.minecraft.world.ticks.TickPriority;

/**
 * Runs one vanilla block routine (a random tick, a scheduled tick, a block change or a falling block's landing) against
 * a bounded, explicitly supplied block view, with a seeded random that records every draw, and records what it did.
 *
 * Usage: TickingBlockFixtures <fixture.json> [name+name...]. Each case's call, setup, origin, state, seed and landing
 * flags are its input; everything else is replaced. With names, only those cases are recorded.
 */
public final class TickingBlockFixtures {
    private static final String NOTES = "Vanilla block routines, recorded by the asset generator's "
        + "TickingBlockFixtures.java against a block view of the case's setup (air elsewhere, void air outside the "
        + "build height of -64 to 319) in the overworld, with a legacy random seeded with seed (0 by default) as both "
        + "the routine's random and the level's. call is randomTick (the origin's state, only when its "
        + "isRandomlyTicking holds, which ticks records), tick (the origin's scheduled tick), set (Level.setBlock of "
        + "state at the origin with UPDATE_ALL) or landing (a falling block of state settling at the origin, with the "
        + "entityDrops rule and silent flag). Writes store the block and, as Level.setBlock does, run the new state's "
        + "onPlace and then, unless UPDATE_KNOWN_SHAPE is set, the neighbours' shape updates; neighbour notifications "
        + "are not run. draws lists each draw in order; writes the block changes with their flags; scheduled the "
        + "block and fluid ticks scheduled with their delays (a block tick counts as pending for hasScheduledTick); "
        + "events the level events; gameEvents the game events with the state they carry; entities the entities "
        + "added, with a falling block's state, position and damage; destroyed the blocks destroyed and whether they "
        + "drop; items the items a landing dropped. after holds the final state of every setup or written position. "
        + "Optional dimension uses that dimension type's environment attributes; actions are later UPDATE_ALL writes. "
        + "call findTip invokes the native speleothem tip search with maxLength and includeMerged, recording tip "
        + "as a position or null.";

    private static final List<String> OUTPUTS = List.of(
        "ticks", "draws", "writes", "scheduled", "events", "gameEvents", "entities", "destroyed", "removed", "items",
        "after", "tip");

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
        if (input.has("dimension")) {
            var registries = (HolderLookup.Provider)VanillaDumper.vanillaRegistries();
            var key = ResourceKey.create(Registries.DIMENSION_TYPE,
                Identifier.parse(input.get("dimension").getAsString()));
            var dimension = registries.lookupOrThrow(Registries.DIMENSION_TYPE).getOrThrow(key).value();
            level.environment = EnvironmentAttributeSystem.builder()
                .addConstantLayer(dimension.attributes())
                .build();
        }

        var touched = new LinkedHashSet<BlockPos>();
        for (var placement : input.getAsJsonArray("setup")) {
            var value = placement.getAsJsonObject();
            var pos = position(value.getAsJsonArray("pos"));
            level.blocks.put(pos, state(value.get("state").getAsString()));
            touched.add(pos);
        }

        var origin = position(input.getAsJsonArray("origin"));
        var call = input.get("call").getAsString();
        switch (call) {
            case "randomTick" -> {
                // The level only random-ticks a state whose isRandomlyTicking holds, so the others draw nothing.
                var state = level.getBlockState(origin);
                var ticks = state.isRandomlyTicking();
                input.addProperty("ticks", ticks);
                if (ticks) {
                    state.randomTick(level, origin, random);
                }
            }
            case "tick" -> level.getBlockState(origin).tick(level, origin, random);
            case "set" -> level.setBlock(origin, state(input.get("state").getAsString()), Block.UPDATE_ALL, 512);
            case "landing" -> land(input, level, origin);
            case "findTip" -> {
                var method = net.minecraft.world.level.block.SpeleothemBlock.class.getDeclaredMethod("findTip",
                    BlockState.class, net.minecraft.world.level.LevelAccessor.class, BlockPos.class,
                    int.class, boolean.class);
                method.setAccessible(true);
                var tip = (BlockPos)method.invoke(null, level.getBlockState(origin), level, origin,
                    input.get("maxLength").getAsInt(), input.get("includeMerged").getAsBoolean());
                input.add("tip", tip == null ? com.google.gson.JsonNull.INSTANCE : coordinates(tip));
            }
            default -> throw new IllegalArgumentException("Unknown call " + call);
        }

        if (input.has("actions")) {
            for (var entry : input.getAsJsonArray("actions")) {
                var action = entry.getAsJsonObject();
                level.setBlock(position(action.getAsJsonArray("pos")),
                    state(action.get("state").getAsString()), Block.UPDATE_ALL, 512);
            }
        }

        input.add("draws", random.draws);
        input.add("writes", level.writes);
        input.add("scheduled", level.scheduled);
        input.add("events", level.events);
        input.add("gameEvents", level.gameEvents);
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

    // A falling block of the case's state that has reached the ground at the origin: vanilla decides whether it
    // places, drops or breaks, and what it plays.
    private static void land(JsonObject input, ProbeLevel level, BlockPos origin) throws Exception {
        level.rules.set(GameRules.ENTITY_DROPS, input.get("entityDrops").getAsBoolean(), null);

        var falling = new LandingProbe(level);
        falling.setPos(origin.getX() + 0.5, origin.getY(), origin.getZ() + 0.5);
        field(FallingBlockEntity.class, "blockState").set(falling, state(input.get("state").getAsString()));
        falling.setSilent(input.get("silent").getAsBoolean());
        falling.tick();

        input.addProperty("removed", falling.isRemoved());
        input.addProperty("items", falling.items);
    }

    private static Field field(Class<?> type, String name) throws Exception {
        var field = type.getDeclaredField(name);
        field.setAccessible(true);
        return field;
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

    private static JsonArray coordinates(Vec3 position) {
        var result = new JsonArray();
        result.add(position.x);
        result.add(position.y);
        result.add(position.z);
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

    /** Pending block ticks as the probe's level recorded them, for vanilla's hasScheduledTick checks. */
    private static final class ProbeBlockTicks extends LevelTicks<Block> {
        private final ProbeLevel level;

        ProbeBlockTicks(ProbeLevel level) {
            super(pos -> true);
            this.level = level;
        }

        @Override public boolean hasScheduledTick(BlockPos pos, Block type) {
            var name = BuiltInRegistries.BLOCK.getKey(type).toString();
            for (var entry : level.scheduled) {
                var tick = entry.getAsJsonObject();
                if (position(tick.getAsJsonArray("pos")).equals(pos) && tick.get("type").getAsString().equals(name)) {
                    return true;
                }
            }

            return false;
        }
    }

    /**
     * The block view: blocks from the case (air elsewhere), the overworld's environment, and the level random being
     * the routine's random, as ServerLevel passes its own random to block ticks.
     */
    private static final class ProbeLevel extends ServerLevel {
        Map<BlockPos, BlockState> blocks;
        JsonArray writes;
        JsonArray scheduled;
        JsonArray events;
        JsonArray gameEvents;
        List<Entity> added;
        JsonArray destroyed;
        RandomSource tickRandom;
        GameRules rules;
        LevelTicks<Block> blockTicks;
        EnvironmentAttributeSystem environment;

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
            level.events = new JsonArray();
            level.gameEvents = new JsonArray();
            level.added = new java.util.ArrayList<>();
            level.destroyed = new JsonArray();
            level.tickRandom = random;
            level.rules = new GameRules(List.of(GameRules.ENTITY_DROPS));
            level.blockTicks = new ProbeBlockTicks(level);
            level.environment = EnvironmentAttributeSystem.builder().build();
            return level;
        }

        @Override public RandomSource getRandom() {
            return tickRandom;
        }

        @Override public GameRules getGameRules() {
            return rules;
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
            recorded.add("pos", coordinates(pos));
            var affected = context.affectedState();
            recorded.addProperty("state", affected == null ? null : name(affected));
            gameEvents.add(recorded);
        }

        // No entities stand in the view, so blocks that push entities up (mud turning to clay, paths to dirt) find
        // none.
        @Override public List<Entity> getEntities(Entity except, AABB box,
            java.util.function.Predicate<? super Entity> predicate) {
            return List.of();
        }

        @Override public boolean addFreshEntity(Entity entity) {
            added.add(entity);
            return true;
        }

        // The entities added, read once the routine is done: a falling stalactite's damage is set after it's added.
        JsonArray entities() throws Exception {
            var result = new JsonArray();
            for (var entity : added) {
                var recorded = new JsonObject();
                recorded.addProperty("type", BuiltInRegistries.ENTITY_TYPE.getKey(entity.getType()).toString());
                recorded.add("pos", coordinates(entity.position()));
                if (entity instanceof FallingBlockEntity falling) {
                    recorded.addProperty("state", name(falling.getBlockState()));
                    recorded.addProperty("hurtEntities",
                        field(FallingBlockEntity.class, "hurtEntities").getBoolean(falling));
                    recorded.addProperty("damagePerDistance",
                        field(FallingBlockEntity.class, "fallDamagePerDistance").getFloat(falling));
                    recorded.addProperty("maxDamage",
                        field(FallingBlockEntity.class, "fallDamageMax").getInt(falling));
                }

                result.add(recorded);
            }

            return result;
        }
    }

    /** Supplies an already resolved ground contact; vanilla still decides placement, drops and break effects. */
    private static final class LandingProbe extends FallingBlockEntity {
        int items;

        LandingProbe(ProbeLevel level) {
            super(EntityTypes.FALLING_BLOCK, level);
        }

        @Override public void move(MoverType type, Vec3 delta) {
            setOnGround(true);
        }

        @Override protected void checkSupportingBlock(boolean onGround, Vec3 movement) {
        }

        @Override protected void applyEffectsFromBlocks() {
        }

        @Override protected void handlePortal() {
        }

        @Override protected void applyGravity() {
        }

        @Override protected float getAirDrag() {
            return 1;
        }

        @Override public net.minecraft.world.entity.item.ItemEntity spawnAtLocation(ServerLevel level, ItemLike item) {
            items++;
            return null;
        }
    }
}
