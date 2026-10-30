import java.io.IOException;
import java.lang.reflect.Constructor;
import java.lang.reflect.Field;
import java.lang.reflect.GenericArrayType;
import java.lang.reflect.InvocationTargetException;
import java.lang.reflect.Method;
import java.lang.reflect.Modifier;
import java.lang.reflect.ParameterizedType;
import java.lang.reflect.Type;
import java.lang.reflect.WildcardType;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Comparator;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.TreeMap;
import java.util.function.Function;
import java.util.function.Predicate;
import java.util.stream.Collectors;

/**
 * Dumps the vanilla data Obsidian needs that Mojang's data generators don't report: per block state light, physics,
 * transforms, map colors and wall shape covers, entity data, and the fields of each packet.
 *
 * <p>Usage, with the unbundled server jar and its libraries on the class path:
 * {@code java -cp <server + libraries> VanillaDumper.java <assets directory> <work directory>}.
 * The assets go to the assets directory; {@code worldgen_groups.json} and {@code packet_fields.json}, which only the
 * generator itself uses, go to the work directory.
 *
 * <p>Everything vanilla is reached through reflection by name (see {@link Vanilla}). The values come from calling
 * vanilla's own methods on every block
 * state or entity type, except how packets are written, which {@link PacketFormats} reads from their bytecode; the file
 * formats are Obsidian's, documented by their readers (named on each dump method).
 *
 * <p>Needs Java 25: the source launcher compiles {@code PacketFormats.java} next to this file, and it uses the
 * class-file API ({@code java.lang.classfile}).
 */
public final class VanillaDumper {
    // Names of the classes used below (also how parameter types are given).
    private static final String BLOCK = "net.minecraft.world.level.block.Block";

    private static final String BLOCK_STATE = "net.minecraft.world.level.block.state.BlockState";

    private static final String STATE_BASE = "net.minecraft.world.level.block.state.BlockBehaviour$BlockStateBase";

    private static final String BLOCK_GETTER = "net.minecraft.world.level.BlockGetter";

    private static final String BLOCK_POS = "net.minecraft.core.BlockPos";

    private static final String DIRECTION = "net.minecraft.core.Direction";

    private static final String AXIS = "net.minecraft.core.Direction$Axis";

    private static final String SUPPORT_TYPE = "net.minecraft.world.level.block.SupportType";

    private static final String VOXEL_SHAPE = "net.minecraft.world.phys.shapes.VoxelShape";

    private static final String SHAPES = "net.minecraft.world.phys.shapes.Shapes";

    private static final String FLUID = "net.minecraft.world.level.material.Fluid";

    private static final String FLUID_STATE = "net.minecraft.world.level.material.FluidState";

    private static final String FLUIDS = "net.minecraft.world.level.material.Fluids";

    private static final String FLOWING_FLUID = "net.minecraft.world.level.material.FlowingFluid";

    private static final String REGISTRY = "net.minecraft.core.Registry";

    private static final String REGISTRIES = "net.minecraft.core.registries.BuiltInRegistries";

    private static final String HOLDER = "net.minecraft.core.Holder";

    private static final String ENTITY = "net.minecraft.world.entity.Entity";

    private static final String ENTITY_TYPE = "net.minecraft.world.entity.EntityType";

    private static final String TAG_KEY = "net.minecraft.tags.TagKey";

    private static final String PACKET_TYPE = "net.minecraft.network.protocol.PacketType";

    private static final String PATH_TYPE = "net.minecraft.world.level.pathfinder.PathType";

    private static final String WALK_NODE = "net.minecraft.world.level.pathfinder.WalkNodeEvaluator";

    private final Path output;

    private final String version;

    // Shared arguments: vanilla's empty world (air everywhere) at the origin, for methods that take a level and
    // position.
    private final Object emptyLevel;

    private final Object origin;

    private final Object[] directions;

    private final List<Object> states = new ArrayList<>();

    private final Method getBlock;

    private final Method getCollisionShape;

    private final Method getFluidState;

    private final Method registryKey;

    private final Object blockRegistry;

    private final Object emptyShape;

    private final Object blockShape;

    // Shape helpers' handles, used for every state.
    private final Method getAxis;

    private final Method shapeMin;

    private final Method shapeMax;

    private final Method toAabbs;

    private final Field[] boxBounds;

    private final Field discreteShape;

    private final Method discreteSize;

    private final Constructor<?> sliceShape;

    public static void main(String[] args) throws Exception {
        if (args.length != 2) {
            System.err.println("Usage: java VanillaDumper.java <assets directory> <work directory>");
            System.exit(1);
        }

        var started = System.nanoTime();
        var output = Path.of(args[0]);
        var work = Path.of(args[1]);
        Files.createDirectories(output);
        Files.createDirectories(work);

        var version = bootstrap();
        var dumper = new VanillaDumper(output, version);
        dumper.dumpBlockLight();
        dumper.dumpBlockSounds();
        dumper.dumpBlockPhysics();
        dumper.dumpBlockPlacementClasses();
        dumper.dumpBlockOverrideClasses();
        dumper.dumpSaplingGrowers();
        dumper.dumpFireOdds();
        dumper.dumpIgnitedByLava();
        dumper.dumpPathTypeMalus();
        dumper.dumpPathTypeStates();
        dumper.dumpTntBlocks();
        dumper.dumpItemUseOnClasses();
        dumper.dumpItemUseClasses();
        dumper.dumpItemClickClasses();
        dumper.dumpItemPlacedBlocks();
        dumper.dumpItemAttachments();
        dumper.dumpCollisionShapes();
        dumper.dumpBlastResistance();
        dumper.dumpBlockLootTables();
        dumper.dumpBlockTransforms();
        dumper.dumpMapColors();
        dumper.dumpWallShapeCovers();
        dumper.dumpEntities();
        dumper.dumpCreativeTabs();
        dumper.dumpStatistics();
        dumper.dumpWorldgenGroups(work);
        dumper.dumpPacketFields(work);
        System.out.printf("Dumped vanilla data in %.1fs%n", (System.nanoTime() - started) / 1e9);
    }

    /**
     * Starts vanilla like its server and data generator do (version info first, then the registries), and binds the
     * built-in registries' tags from vanilla's data pack and then their default components (an item's, for example):
     * some block methods check tags (e.g. {@code BlockTags.SIGNS} in {@code FlowingFluid.canHoldAnyFluid}), some block
     * entities create item stacks, and both are only bound when data packs load.
     */
    static String bootstrap() {
        var sharedConstants = "net.minecraft.SharedConstants";
        Vanilla.call(Vanilla.method(sharedConstants, "tryDetectVersion"), null);
        Vanilla.call(Vanilla.method("net.minecraft.server.Bootstrap", "bootStrap"), null);

        var packType = "net.minecraft.server.packs.PackType";
        var vanillaPack = Vanilla.call(
            Vanilla.method("net.minecraft.server.packs.VanillaPackResources", "fullResources"),
            Vanilla.call(
                Vanilla.method("net.minecraft.server.packs.repository.ServerPacksSource", "createVanillaPackSource"),
                null));
        var resources = Vanilla.create(
            Vanilla.constructor(
                "net.minecraft.server.packs.resources.MultiPackResourceManager",
                packType,
                "java.util.List"),
            Vanilla.get(packType, "SERVER_DATA"),
            List.of(vanillaPack));

        var loadPendingTags = Vanilla.method(
            "net.minecraft.tags.TagLoader",
            "loadPendingTags",
            "net.minecraft.server.packs.resources.ResourceManager",
            REGISTRY);
        var pendingTags = "net.minecraft.core.Registry$PendingTags";
        var size = Vanilla.method(pendingTags, "size");
        var apply = Vanilla.method(pendingTags, "apply");
        var bound = 0;
        for (var registry : (Iterable<?>) Vanilla.get(REGISTRIES, "REGISTRY")) {
            var pending = (Optional<?>) Vanilla.call(loadPendingTags, null, resources, registry);
            if (pending.isPresent()) {
                bound += (int) Vanilla.call(size, pending.get());
                Vanilla.call(apply, pending.get());
            }
        }
        if (bound == 0) {
            throw new IllegalStateException("Vanilla's data pack has no tags");
        }
        bindComponents(vanillaRegistries());

        var currentVersion = Vanilla.call(Vanilla.method(sharedConstants, "getCurrentVersion"), null);
        return (String) Vanilla.call(Vanilla.method("net.minecraft.WorldVersion", "name"), currentVersion);
    }

    /**
     * Vanilla's data-driven registries as its data generator builds them: the world registries (biomes, enchantments,
     * ...) and the reloadable ones (loot tables, recipes, ...) on top.
     */
    static Object vanillaRegistries() {
        var registries = "net.minecraft.data.registries.VanillaRegistries";
        var world = Vanilla.call(Vanilla.method(registries, "createWorldLookup"), null);
        return Vanilla.call(
            Vanilla.method(registries, "createReloadableLookup", "net.minecraft.core.HolderLookup$Provider"),
            null,
            world);
    }

    /**
     * Binds the built-in registries' default components ({@code BuiltInRegistries.DATA_COMPONENT_INITIALIZERS}), which
     * may refer to entries of the given registries, as a server does once its data packs loaded.
     */
    static void bindComponents(Object registries) {
        var initializers = Vanilla.get(REGISTRIES, "DATA_COMPONENT_INITIALIZERS");
        var build = Vanilla.method(
            "net.minecraft.core.component.DataComponentInitializers",
            "build",
            "net.minecraft.core.HolderLookup$Provider");
        var apply = Vanilla.method("net.minecraft.core.component.DataComponentInitializers$PendingComponents", "apply");
        for (var pending : (List<?>) Vanilla.call(build, initializers, registries)) {
            Vanilla.call(apply, pending);
        }
    }

    /** Records aliases such as wall torches, and blocks that deliberately have no drop table. */
    private void dumpBlockLootTables() throws IOException {
        var tableOf = Vanilla.method("net.minecraft.world.level.block.state.BlockBehaviour", "getLootTable");
        var keyId = Vanilla.method("net.minecraft.resources.ResourceKey", "identifier");
        var tables = new TreeMap<String, Object>();
        for (var block : (Iterable<?>) blockRegistry) {
            var id = Vanilla.call(registryKey, blockRegistry, block).toString();
            var table = (Optional<?>) Vanilla.call(tableOf, block);
            tables.put(id, table.map(key -> Vanilla.call(keyId, key).toString()).orElse(null));
        }

        Json.write(output.resolve("block_loot_tables.json"), tables, false);
    }

    private VanillaDumper(Path output, String version) {
        this.output = output;
        this.version = version;
        emptyLevel = Vanilla.get("net.minecraft.world.level.EmptyBlockGetter", "INSTANCE");
        origin = Vanilla.get(BLOCK_POS, "ZERO");
        directions = (Object[]) Vanilla.call(Vanilla.method(DIRECTION, "values"), null);
        getBlock = Vanilla.method(STATE_BASE, "getBlock");
        getCollisionShape = Vanilla.method(STATE_BASE, "getCollisionShape", BLOCK_GETTER, BLOCK_POS);
        getFluidState = Vanilla.method(STATE_BASE, "getFluidState");
        registryKey = Vanilla.method(REGISTRY, "getKey", "java.lang.Object");
        blockRegistry = Vanilla.get(REGISTRIES, "BLOCK");
        emptyShape = Vanilla.call(Vanilla.method(SHAPES, "empty"), null);
        blockShape = Vanilla.call(Vanilla.method(SHAPES, "block"), null);

        getAxis = Vanilla.method(DIRECTION, "getAxis");
        shapeMin = Vanilla.method(VOXEL_SHAPE, "min", AXIS);
        shapeMax = Vanilla.method(VOXEL_SHAPE, "max", AXIS);
        toAabbs = Vanilla.method(VOXEL_SHAPE, "toAabbs");
        var aabb = "net.minecraft.world.phys.AABB";
        boxBounds = new Field[] {
            Vanilla.field(aabb, "minX"),
            Vanilla.field(aabb, "minY"),
            Vanilla.field(aabb, "minZ"),
            Vanilla.field(aabb, "maxX"),
            Vanilla.field(aabb, "maxY"),
            Vanilla.field(aabb, "maxZ")};
        discreteShape = Vanilla.field(VOXEL_SHAPE, "shape");
        discreteSize = Vanilla.method("net.minecraft.world.phys.shapes.DiscreteVoxelShape", "getSize", AXIS);
        sliceShape = Vanilla.constructor("net.minecraft.world.phys.shapes.SliceShape", VOXEL_SHAPE, AXIS, "int");

        // Block.BLOCK_STATE_REGISTRY holds every state by its id, which is what Obsidian's per-state tables index by.
        var stateIds = Vanilla.get(BLOCK, "BLOCK_STATE_REGISTRY");
        var byId = Vanilla.method("net.minecraft.core.IdMapper", "byId", "int");
        var size = (int) Vanilla.call(Vanilla.method("net.minecraft.core.IdMapper", "size"), stateIds);
        for (var id = 0; id < size; id++) {
            states.add(Vanilla.call(byId, stateIds, id));
        }
    }

    /** State-indexed audio behavior, kept as generated data rather than hand-maintained material guesses. */
    private void dumpBlockSounds() throws IOException {
        var soundType = "net.minecraft.world.level.block.SoundType";
        var getType = Vanilla.method(STATE_BASE, "getSoundType");
        var volume = Vanilla.method(soundType, "getVolume");
        var pitch = Vanilla.method(soundType, "getPitch");
        var location = Vanilla.method("net.minecraft.sounds.SoundEvent", "location");
        var types = new Indexer<Map<String, Object>>();
        var ids = new int[states.size()];
        for (var i = 0; i < states.size(); i++) {
            var type = Vanilla.call(getType, states.get(i));
            var row = new LinkedHashMap<String, Object>();
            row.put("volume", Vanilla.call(volume, type));
            row.put("pitch", Vanilla.call(pitch, type));
            for (var kind : List.of("Break", "Step", "Place", "Hit", "Fall")) {
                var event = Vanilla.call(Vanilla.method(soundType, "get" + kind + "Sound"), type);
                row.put(kind.toLowerCase(java.util.Locale.ROOT), Vanilla.call(location, event).toString());
            }
            ids[i] = types.indexOf(row);
        }
        Json.write(output.resolve("block_sounds.json"), Map.of("states", ids, "types", types.values()), false);
    }

    /**
     * {@code block_light.json}, read by {@code Obsidian/Registries/BlockLight.cs}: per state, {@code
     * getLightDampening()}
     * plus a 1-based index into {@code shapes} for the states whose shape occludes light, each shape listing its six
     * face masks ({@code LightEngine.getOcclusionShape} per direction) as indexes into {@code faces}.
     */
    private void dumpBlockLight() throws IOException {
        var getLightDampening = Vanilla.method(STATE_BASE, "getLightDampening");
        var lightEngine = "net.minecraft.world.level.lighting.LightEngine";
        // States that don't use their shape for light occlusion have only empty occlusion shapes (isEmptyShape).
        var isEmptyShape = Vanilla.method(lightEngine, "isEmptyShape", BLOCK_STATE);
        var getOcclusionShape = Vanilla.method(lightEngine, "getOcclusionShape", BLOCK_STATE, DIRECTION);

        var light = new int[states.size()];
        var faces = new Indexer<String>();
        var shapes = new Indexer<List<Integer>>();
        var shapeStates = new ArrayList<Object>(); // one state per shape, to check the masks against vanilla below
        for (var id = 0; id < states.size(); id++) {
            var state = states.get(id);
            light[id] = (int) Vanilla.call(getLightDampening, state);
            if ((boolean) Vanilla.call(isEmptyShape, null, state)) {
                continue;
            }

            var shapeFaces = new ArrayList<Integer>();
            for (var direction : directions) {
                shapeFaces.add(
                    faces.indexOf(faceMask(Vanilla.call(getOcclusionShape, null, state, direction), direction)));
            }

            var shape = shapes.indexOf(shapeFaces);
            if (shape == shapeStates.size()) {
                shapeStates.add(state);
            }
            light[id] |= (shape + 1) << 4;
        }

        checkLightMasks(shapeStates, shapes.values(), faces.values());

        var json = new LinkedHashMap<String, Object>();
        json.put(
            "_notes",
            "Vanilla " + version
                + " block state light properties, indexed by state id. See BlockLight for the layout.");
        json.put("light", light);
        json.put("shapes", shapes.values());
        json.put("faces", faces.values());
        Json.write(output.resolve("block_light.json"), json, false);
    }

    /**
     * A face shape as a 16x16 grid of 1/16 cells, one bit per cell whose center the shape covers, in hex. Rows run
     * along
     * the first of the face's two axes in x, y, z order and columns along the second; row r is bits 16r to 16r+15 of
     * the
     * 256-bit mask, written as four 64-bit words, lowest first.
     */
    private String faceMask(Object faceShape, Object direction) {
        var axes = faceAxes(direction);
        var words = new long[4];
        for (var box : boxes(faceShape)) {
            for (var row = 0; row < 16; row++) {
                var u = (row + 0.5) / 16;
                if (u < box[axes[0]] || u > box[axes[0] + 3]) {
                    continue;
                }
                for (var column = 0; column < 16; column++) {
                    var v = (column + 0.5) / 16;
                    if (v >= box[axes[1]] && v <= box[axes[1] + 3]) {
                        var bit = row * 16 + column;
                        words[bit >> 6] |= 1L << (bit & 63);
                    }
                }
            }
        }

        return String.format("%016x%016x%016x%016x", words[0], words[1], words[2], words[3]);
    }

    /**
     * Checks that the masks reproduce vanilla: for every pair of light-occluding shapes and every direction, two
     * touching faces block light exactly when their masks together fill the grid. Vanilla's check
     * ({@code LightEngine.shapeOccludes}) is {@code Shapes.faceShapeOccludes} of the two faces' occlusion shapes.
     */
    private void checkLightMasks(List<Object> shapeStates, List<List<Integer>> shapes, List<String> faces) {
        var getOcclusionShape = Vanilla.method(
            "net.minecraft.world.level.lighting.LightEngine",
            "getOcclusionShape",
            BLOCK_STATE,
            DIRECTION);
        var faceShapeOccludes = Vanilla.method(SHAPES, "faceShapeOccludes", VOXEL_SHAPE, VOXEL_SHAPE);
        var masks = faces.stream().map(VanillaDumper::maskWords).toList();
        for (var from = 0; from < shapes.size(); from++) {
            for (var to = 0; to < shapes.size(); to++) {
                for (var direction = 0; direction < 6; direction++) {
                    var fromMask = masks.get(shapes.get(from).get(direction));
                    var toMask = masks.get(shapes.get(to).get(direction ^ 1)); // opposite directions differ in bit 0
                    var covered = true;
                    for (var word = 0; word < 4; word++) {
                        covered &= (fromMask[word] | toMask[word]) == -1L;
                    }

                    var vanilla = (boolean) Vanilla.call(
                        faceShapeOccludes,
                        null,
                        Vanilla.call(getOcclusionShape, null, shapeStates.get(from), directions[direction]),
                        Vanilla.call(getOcclusionShape, null, shapeStates.get(to), directions[direction ^ 1]));
                    if (covered != vanilla) {
                        throw new IllegalStateException(
                            "Light face masks don't match LightEngine.shapeOccludes for shapes " + from + " and " + to);
                    }
                }
            }
        }
    }

    /** Collision boxes by block state, read by Obsidian/Entities/AI/BlockCollisionShapes.cs. */
    private void dumpCollisionShapes() throws IOException {
        var shapes = new Indexer<List<double[]>>(
            shape -> shape.stream().map(box -> Arrays.stream(box).boxed().toList()).toList());
        var stateShapes = new LinkedHashMap<String, Integer>();
        for (var id = 0; id < states.size(); id++) {
            var shape = Vanilla.call(getCollisionShape, states.get(id), emptyLevel, origin);
            stateShapes.put(Integer.toString(id), shapes.indexOf(boxes(shape)));
        }

        var shapeDefinitions = new LinkedHashMap<String, List<double[]>>();
        for (var id = 0; id < shapes.values().size(); id++) {
            shapeDefinitions.put(Integer.toString(id), shapes.values().get(id));
        }

        Json.write(
            output.resolve("collision_shapes.json"),
            Map.of("shapes", shapeDefinitions, "states", stateShapes),
            false);
    }

    /** Block blast resistance, read by Obsidian/WorldData/AbstractLevel.Explosion.cs. */
    private void dumpBlastResistance() throws IOException {
        var getExplosionResistance = Vanilla.method(BLOCK, "getExplosionResistance");
        var resistance = new LinkedHashMap<String, Float>();
        for (var block : (Iterable<?>) blockRegistry) {
            resistance.put(key(blockRegistry, block), (float) Vanilla.call(getExplosionResistance, block));
        }

        Json.write(output.resolve("blast_resistance.json"), resistance, false);
    }

    private static long[] maskWords(String hex) {
        var words = new long[4];
        for (var word = 0; word < 4; word++) {
            words[word] = Long.parseUnsignedLong(hex.substring(word * 16, word * 16 + 16), 16);
        }
        return words;
    }

    /**
     * {@code block_physics.json}, read by {@code Obsidian/Registries/BlockPhysics.cs}, which documents the bit layouts
     * of
     * {@code flags} and {@code fluidFlags}. Each bit is the vanilla method named next to it below, called on the state
     * in
     * the empty world at the origin.
     */
    private void dumpBlockPhysics() throws IOException {
        new BlockPhysicsDump().dump();
    }

    /** The reflection handles, shared shape indexes and per-state results of one physics dump. */
    private final class BlockPhysicsDump {
        private final Method isAir = Vanilla.method(STATE_BASE, "isAir");
        // BlockStateBase.blocksMotion became the blocks_motion tag in 26.1.
        private final Object blocksMotion = Vanilla.get("net.minecraft.tags.BlockTags", "BLOCKS_MOTION");

        private final Method isSolid = Vanilla.method(STATE_BASE, "isSolid");

        private final Method canBeReplaced = Vanilla.method(STATE_BASE, "canBeReplaced");

        private final Method liquid = Vanilla.method(STATE_BASE, "liquid");

        private final Method isCollisionShapeFullBlock =
            Vanilla.method(STATE_BASE, "isCollisionShapeFullBlock", BLOCK_GETTER, BLOCK_POS);

        private final Method isFaceSturdy =
            Vanilla.method(STATE_BASE, "isFaceSturdy", BLOCK_GETTER, BLOCK_POS, DIRECTION, SUPPORT_TYPE);

        private final Method isRedstoneConductor =
            Vanilla.method(STATE_BASE, "isRedstoneConductor", BLOCK_GETTER, BLOCK_POS);

        private final Method hasBlockEntity = Vanilla.method(STATE_BASE, "hasBlockEntity");

        private final Method getLightEmission = Vanilla.method(STATE_BASE, "getLightEmission");

        private final Method isSolidRender = Vanilla.method(STATE_BASE, "isSolidRender");

        private final Method isSource = Vanilla.method(FLUID_STATE, "isSource");

        private final Method getAmount = Vanilla.method(FLUID_STATE, "getAmount");

        private final Method getType = Vanilla.method(FLUID_STATE, "getType");

        private final Method isEmpty = Vanilla.method(VOXEL_SHAPE, "isEmpty");

        private final Method isFaceFull = Vanilla.method(BLOCK, "isFaceFull", VOXEL_SHAPE, DIRECTION);

        private final Method is = Vanilla.method("net.minecraft.core.TypedInstance", "is", TAG_KEY);

        private final Object unstableBottomCenter =
            Vanilla.get("net.minecraft.tags.BlockTags", "UNSTABLE_BOTTOM_CENTER");

        private final Object full = Vanilla.get(SUPPORT_TYPE, "FULL");

        private final Object center = Vanilla.get(SUPPORT_TYPE, "CENTER");

        private final Object rigid = Vanilla.get(SUPPORT_TYPE, "RIGID");

        private final Object up = Vanilla.get(DIRECTION, "UP");

        private final Object down = Vanilla.get(DIRECTION, "DOWN");

        private final Class<?> liquidBlockContainer =
            Vanilla.type("net.minecraft.world.level.block.LiquidBlockContainer");

        private final Method canHoldAnyFluid = Vanilla.method(FLOWING_FLUID, "canHoldAnyFluid", BLOCK_STATE);

        private final Method canHoldSpecificFluid =
            Vanilla.method(FLOWING_FLUID, "canHoldSpecificFluid", BLOCK_GETTER, BLOCK_POS, BLOCK_STATE, FLUID);
        // Obsidian's FluidKind: 1-4 are these fluids (0 is empty). Not vanilla's registry ids, which order flowing
        // first.
        private final Object[] fluids = new Object[] {
            null,
            Vanilla.get(FLUIDS, "WATER"),
            Vanilla.get(FLUIDS, "FLOWING_WATER"),
            Vanilla.get(FLUIDS, "LAVA"),
            Vanilla.get(FLUIDS, "FLOWING_LAVA")};

        private final Method visualShape = Vanilla.method(
            STATE_BASE,
            "getVisualShape",
            BLOCK_GETTER,
            BLOCK_POS,
            "net.minecraft.world.phys.shapes.CollisionContext");

        private final Method outlineShape = Vanilla.method(
            STATE_BASE,
            "getShape",
            BLOCK_GETTER,
            BLOCK_POS,
            "net.minecraft.world.phys.shapes.CollisionContext");

        private final Method isSuffocating = Vanilla.method(STATE_BASE, "isSuffocating", BLOCK_GETTER, BLOCK_POS);

        private final Method getPostProcessPos =
            Vanilla.method(STATE_BASE, "getPostProcessPos", BLOCK_GETTER, BLOCK_POS);

        private final Method hasOffset = Vanilla.method(STATE_BASE, "hasOffsetFunction");

        private final Method getOffset = Vanilla.method(STATE_BASE, "getOffset", BLOCK_POS);

        private final Method maxHorizontalOffset =
            Vanilla.method("net.minecraft.world.level.block.state.BlockBehaviour", "getMaxHorizontalOffset");

        private final Method maxVerticalOffset =
            Vanilla.method("net.minecraft.world.level.block.state.BlockBehaviour", "getMaxVerticalOffset");

        private final Field offsetX = Vanilla.field("net.minecraft.world.phys.Vec3", "x");

        private final Field offsetY = Vanilla.field("net.minecraft.world.phys.Vec3", "y");

        private final Field offsetZ = Vanilla.field("net.minecraft.world.phys.Vec3", "z");

        private final Object emptyContext =
            Vanilla.call(Vanilla.method("net.minecraft.world.phys.shapes.CollisionContext", "empty"), null);

        private final Method destroySpeed = Vanilla.method(STATE_BASE, "getDestroySpeed", BLOCK_GETTER, BLOCK_POS);

        private final Method correctTool = Vanilla.method(STATE_BASE, "requiresCorrectToolForDrops");

        private final Method isRandomlyTicking = Vanilla.method(STATE_BASE, "isRandomlyTicking");
        // BlockStateBase.getPistonPushReaction: the PushReaction enum, dumped by constant name in ordinal order.
        private final Method pistonPushReaction = Vanilla.method(STATE_BASE, "getPistonPushReaction");

        private final List<String> pushReactionNames = new ArrayList<String>();

        private final int[] pushReactions = new int[states.size()];
        // PistonBaseBlock.isSticky is set by its constructor; the sticky piston and normal piston share one class.
        private final List<Integer> stickyPistonStates = new ArrayList<Integer>();

        private final Field pistonBase =
            Vanilla.field("net.minecraft.world.level.block.piston.PistonBaseBlock", "isSticky");

        private final Class<?> pistonBaseType = Vanilla.type("net.minecraft.world.level.block.piston.PistonBaseBlock");

        private final Method isFluidRandomlyTicking = Vanilla.method(FLUID_STATE, "isRandomlyTicking");

        private final Indexer<List<double[]>> shapeBoxes =
            new Indexer<List<double[]>>(shape -> shape.stream().map(Arrays::toString).toList());

        private final int[] collisionShapes = new int[states.size()];

        private final int[] visualShapes = new int[states.size()];

        private final int[] outlineShapes = new int[states.size()];

        private final List<Boolean> suffocating = new ArrayList<Boolean>();
        // Whether BlockStateBase.getPostProcessPos isn't null: WorldGenRegion marks such states for post-processing
        // they are placed in worldgen.
        private final List<Boolean> postProcess = new ArrayList<Boolean>();

        private final double[][] shapeOffsets = new double[states.size()][];

        private final double[] hardness = new double[states.size()];

        private final List<Boolean> requiresTool = new ArrayList<Boolean>();

        private final List<Integer> randomTicking = new ArrayList<Integer>();

        private final int[] flags = new int[states.size()];
        // Six faces per mode (FULL, CENTER, RIGID), independent of collision and tag-based attachment restrictions.
        private final int[] supportFlags = new int[states.size()];

        private final Object[] supportTypes = new Object[] {full, center, rigid};

        private final int[] fluidFlags = new int[states.size()];

        private final Indexer<List<double[]>> faces =
            new Indexer<List<double[]>>(face -> face.stream().map(Arrays::toString).toList());

        private final Indexer<List<Integer>> faceSets = new Indexer<List<Integer>>();

        void dump() throws IOException {
            faceSets.indexOf(List.of(0, 0, 0, 0, 0, 0)); // set 0: no faces, also used for full blocks (checked first)

            captureStates();
            write();
        }

        private void captureStates() {
            for (var id = 0; id < states.size(); id++) {
                captureState(id);
            }
        }

        private void captureState(int id) {
            var state = states.get(id);
            var fluidState = Vanilla.call(getFluidState, state);
            var collision = Vanilla.call(getCollisionShape, state, emptyLevel, origin);
            collisionShapes[id] = shapeBoxes.indexOf(boxes(collision));
            var visual = Vanilla.call(visualShape, state, emptyLevel, origin, emptyContext);
            visualShapes[id] = shapeBoxes.indexOf(boxes(visual));

            var outline = Vanilla.call(outlineShape, state, emptyLevel, origin, emptyContext);
            outlineShapes[id] = shapeBoxes.indexOf(boxes(outline));

            suffocating.add((boolean) Vanilla.call(isSuffocating, state, emptyLevel, origin));
            postProcess.add(Vanilla.call(getPostProcessPos, state, emptyLevel, origin) != null);
            shapeOffsets[id] = new double[0];
            if ((boolean) Vanilla.call(hasOffset, state)) {
                var offset = Vanilla.call(getOffset, state, origin);
                var block = Vanilla.call(getBlock, state);
                shapeOffsets[id] = new double[] {
                    (double) Vanilla.get(offsetX, offset),
                    (double) Vanilla.get(offsetY, offset),
                    (double) Vanilla.get(offsetZ, offset),
                    (float) Vanilla.call(maxHorizontalOffset, block),
                    (float) Vanilla.call(maxVerticalOffset, block)};
            }

            hardness[id] = (float) Vanilla.call(destroySpeed, state, emptyLevel, origin);
            var reaction = (Enum<?>) Vanilla.call(pistonPushReaction, state);
            if (!pushReactionNames.contains(reaction.name())) {
                pushReactionNames.add(reaction.name());
            }
            pushReactions[id] = pushReactionNames.indexOf(reaction.name());

            var block = Vanilla.call(getBlock, state);
            if (pistonBaseType.isInstance(block) && (boolean) Vanilla.get(pistonBase, block)) {
                stickyPistonStates.add(id);
            }

            requiresTool.add((boolean) Vanilla.call(correctTool, state));

            // What a chunk section counts to decide whether random ticks sample it: the block or its fluid ticks.
            var blockTicks = (boolean) Vanilla.call(isRandomlyTicking, state);
            if (blockTicks || (boolean) Vanilla.call(isFluidRandomlyTicking, fluidState)) {
                randomTicking.add(id);
            }

            flags[id] = blockFlags(state, fluidState, collision);

            captureSupportFlags(id, state);

            fluidFlags[id] = fluidFlags(state, collision);
        }

        private int blockFlags(Object state, Object fluidState, Object collision) {
            var bits = 0;
            bits |= bit(0, Vanilla.call(isAir, state));
            bits |= bit(1, Vanilla.call(is, state, blocksMotion));
            bits |= bit(2, Vanilla.call(isSolid, state));
            bits |= bit(3, Vanilla.call(canBeReplaced, state));
            bits |= bit(4, Vanilla.call(liquid, state));
            bits |= bit(5, Vanilla.call(isSource, fluidState));
            bits |= bit(6, Vanilla.call(isCollisionShapeFullBlock, state, emptyLevel, origin));
            bits |= bit(7, Vanilla.call(isFaceSturdy, state, emptyLevel, origin, up, center));
            bits |= bit(8, Vanilla.call(isFaceSturdy, state, emptyLevel, origin, up, rigid));
            bits |= bit(9, Vanilla.call(isRedstoneConductor, state, emptyLevel, origin));
            bits |= bit(10, Vanilla.call(hasBlockEntity, state));
            for (var face = 0; face < 6; face++) {
                bits |= bit(11 + face, Vanilla.call(isFaceSturdy, state, emptyLevel, origin, directions[face], full));
            }
            bits |= (int) Vanilla.call(getLightEmission, state) << 17;
            bits |= (int) Vanilla.call(getAmount, fluidState) << 21;
            bits |= indexOf(fluids, Vanilla.call(getType, fluidState)) << 25;
            bits |= bit(28, Vanilla.call(isEmpty, collision));
            bits |= bit(29, Vanilla.call(isFaceFull, null, collision, up));
            bits |= bit(30, Vanilla.call(isSolidRender, state));
            // Block.canSupportCenter(level, pos, DOWN), which needs a LevelReader; its body is this check.
            var unstable = (boolean) Vanilla.call(is, state, unstableBottomCenter);
            bits |= bit(31, !unstable && (boolean) Vanilla.call(isFaceSturdy, state, emptyLevel, origin, down, center));
            return bits;
        }

        private void captureSupportFlags(int id, Object state) {
            for (var support = 0; support < supportTypes.length; support++) {
                for (var face = 0; face < directions.length; face++) {
                    supportFlags[id] |= bit(
                        support * 6 + face,
                        Vanilla.call(isFaceSturdy, state, emptyLevel, origin, directions[face], supportTypes[support]));
                }
            }
        }

        private int fluidFlags(Object state, Object collision) {
            var fluidBits = 0;
            fluidBits |= bit(0, collision == blockShape);
            fluidBits |= bit(1, collision == emptyShape);
            fluidBits |= bit(2, liquidBlockContainer.isInstance(Vanilla.call(getBlock, state)));
            fluidBits |= bit(3, Vanilla.call(canHoldAnyFluid, null, state));
            for (var fluid = 1; fluid < fluids.length; fluid++) {
                fluidBits |=
                    bit(3 + fluid, Vanilla.call(canHoldSpecificFluid, null, emptyLevel, origin, state, fluids[fluid]));
            }

            if (collision != blockShape) {
                var set = new ArrayList<Integer>();
                for (var direction : directions) {
                    set.add(faces.indexOf(collisionFace(collision, direction)));
                }
                fluidBits |= faceSets.indexOf(set) << 16;
            }

            return fluidBits;
        }

        private void write() throws IOException {
            var json = new LinkedHashMap<String, Object>();
            json.put(
                "_notes",
                "Vanilla " + version
                    + " block state physics, indexed by state id. See BlockPhysics for the bit layouts.");
            json.put("flags", flags);
            json.put("supportFlags", supportFlags);

            // Block classes, block entities and signal sources.
            json.put("blockClasses", blockClasses());
            var blockEntityTypes = blockEntityTypes();
            json.put("blockEntityTypes", blockEntityTypes);
            json.put("blockEntityTypeIds", blockEntityTypeIds(blockEntityTypes));
            json.put("signalSources", signalSources());
            json.put("fluidFlags", fluidFlags);

            // Shapes: collision faces and boxes, then each state's shapes and offsets.
            json.put("collisionFaceSets", faceSets.values());
            json.put("collisionFaces", faces.values());
            json.put("shapeBoxes", shapeBoxes.values());
            json.put("collisionShapes", collisionShapes);
            json.put("visualShapes", visualShapes);
            json.put("outlineShapes", outlineShapes);
            json.put("suffocating", suffocating);
            json.put("postProcess", postProcess);
            json.put("shapeOffsets", Arrays.asList(shapeOffsets));

            // Breaking, ticking and pistons.
            json.put("hardness", hardness);
            json.put("requiresTool", requiresTool);
            json.put("randomTicking", randomTicking);
            json.put("pushReactionNames", pushReactionNames);
            json.put("pushReactions", pushReactions);
            json.put("stickyPistonStates", stickyPistonStates);

            Json.write(output.resolve("block_physics.json"), json, false);
        }
    }

    /**
     * Where a collision shape touches the given face of its block, as the boxes (min and max on the face's two axes)
     * of the slice that {@code Shapes.mergedFaceOccludes} takes: the shape's outermost layer of cells on that side, or
     * nothing when the shape doesn't reach the side.
     */
    private List<double[]> collisionFace(Object shape, Object direction) {
        var axis = Vanilla.call(getAxis, direction);
        var positive = isPositive(direction);
        var edge = (double) Vanilla.call(positive ? shapeMax : shapeMin, shape, axis);
        if (Math.abs(edge - (positive ? 1 : 0)) > 1.0E-7) {
            return List.of();
        }

        var size = (int) Vanilla.call(discreteSize, Vanilla.get(discreteShape, shape), axis);
        var slice = Vanilla.create(sliceShape, shape, axis, positive ? size - 1 : 0);

        var axes = faceAxes(direction);
        var face = new ArrayList<double[]>();
        for (var box : boxes(slice)) {
            face.add(new double[] {box[axes[0]], box[axes[1]], box[axes[0] + 3], box[axes[1] + 3]});
        }
        return face;
    }

    /**
     * {@code block_placement_classes.json}: the vanilla class that supplies each block's
     * {@code getStateForPlacement} override. Blocks inheriting the base no-op implementation are omitted.
     */
    private void dumpBlockPlacementClasses() throws IOException {
        var context = "net.minecraft.world.item.context.BlockPlaceContext";
        var baseMethod = Vanilla.method(BLOCK, "getStateForPlacement", context);
        var parameter = Vanilla.type(context);
        var classes = new TreeMap<String, String>();

        for (var block : (Iterable<?>) blockRegistry) {
            var owner = block.getClass();
            while (owner != null) {
                try {
                    owner.getDeclaredMethod(baseMethod.getName(), parameter);
                    break;
                } catch (NoSuchMethodException ignored) {
                    owner = owner.getSuperclass();
                }
            }

            if (owner != null && owner != Vanilla.type(BLOCK)) {
                classes.put(key(blockRegistry, block), owner.getName());
            }
        }

        Json.write(output.resolve("block_placement_classes.json"), classes, false);
    }

    /**
     * {@code block_random_tick_classes.json}, {@code block_tick_classes.json},
     * {@code block_update_shape_classes.json} and the other hook-owner files: the vanilla class that supplies each
     * block's override. Blocks inheriting the base no-op are omitted.
     */
    private void dumpBlockOverrideClasses() throws IOException {
        var level = "net.minecraft.server.level.ServerLevel";
        var random = "net.minecraft.util.RandomSource";
        dumpBlockOverrideClasses("block_random_tick_classes.json", "randomTick", BLOCK_STATE, level, BLOCK_POS, random);
        dumpBlockOverrideClasses("block_tick_classes.json", "tick", BLOCK_STATE, level, BLOCK_POS, random);
        dumpBlockOverrideClasses(
            "block_update_shape_classes.json",
            "updateShape",
            BLOCK_STATE,
            "net.minecraft.world.level.LevelReader",
            "net.minecraft.world.level.ScheduledTickAccess",
            BLOCK_POS,
            "net.minecraft.core.Direction",
            BLOCK_POS,
            BLOCK_STATE,
            random);
        dumpBlockOverrideClasses(
            "block_entity_inside_classes.json",
            "entityInside",
            BLOCK_STATE,
            "net.minecraft.world.level.Level",
            BLOCK_POS,
            ENTITY,
            "net.minecraft.world.entity.InsideBlockEffectApplier",
            "boolean");
        dumpBlockOverrideClasses(
            "block_on_place_classes.json",
            "onPlace",
            BLOCK_STATE,
            "net.minecraft.world.level.Level",
            BLOCK_POS,
            BLOCK_STATE,
            "boolean");
    }

    /**
     * {@code fire_odds.json}: the ignite and burn odds {@code FireBlock.bootStrap} registers for each flammable block,
     * read from the fire block's {@code igniteOdds} and {@code burnOdds} maps. Blocks it never registered are omitted.
     */
    private void dumpFireOdds() throws IOException {
        var fire = Vanilla.type("net.minecraft.world.level.block.FireBlock");
        var igniteField = Vanilla.field("net.minecraft.world.level.block.FireBlock", "igniteOdds");
        var burnField = Vanilla.field("net.minecraft.world.level.block.FireBlock", "burnOdds");
        var odds = new TreeMap<String, Map<String, Object>>();

        for (var block : (Iterable<?>) blockRegistry) {
            if (!fire.isInstance(block)) {
                continue;
            }

            @SuppressWarnings("unchecked") var ignite = (Map<Object, Object>) Vanilla.get(igniteField, block);
            @SuppressWarnings("unchecked") var burn = (Map<Object, Object>) Vanilla.get(burnField, block);
            for (var entry : ignite.entrySet()) {
                var row = new TreeMap<String, Object>();
                row.put("ignite", entry.getValue());
                row.put("burn", burn.getOrDefault(entry.getKey(), 0));
                odds.put(key(blockRegistry, entry.getKey()), row);
            }
        }

        Json.write(output.resolve("fire_odds.json"), odds, false);
    }

    /**
     * {@code ignited_by_lava.json}: the blocks whose default state {@code BlockStateBase.ignitedByLava()} is true, the
     * flammable set {@code LavaFluid.randomTick} sets fire next to. Keyed by block, with the value {@code true}.
     */
    private void dumpIgnitedByLava() throws IOException {
        var ignited = Vanilla.method(STATE_BASE, "ignitedByLava");
        var defaultState = Vanilla.method(BLOCK, "defaultBlockState");
        var blocks = new TreeMap<String, Boolean>();

        for (var block : (Iterable<?>) blockRegistry) {
            if ((boolean) Vanilla.call(ignited, Vanilla.call(defaultState, block))) {
                blocks.put(key(blockRegistry, block), true);
            }
        }

        Json.write(output.resolve("ignited_by_lava.json"), blocks, false);
    }

    /**
     * Dumps vanilla's {@code PathType} constants with their default malus, in declaration order. Entries are keyed by
     * the enum constant's name, which is what the pathfinder's classification refers to.
     */
    private void dumpPathTypeMalus() throws IOException {
        var getMalus = Vanilla.method(PATH_TYPE, "getMalus");
        var malus = new LinkedHashMap<String, Double>();
        for (var value : (Enum<?>[]) Vanilla.call(Vanilla.method(PATH_TYPE, "values"), null)) {
            malus.put(value.name(), ((Number) Vanilla.call(getMalus, value)).doubleValue());
        }

        Json.write(output.resolve("path_type_malus.json"), malus, false);
    }

    /**
     * Dumps vanilla's own-state pathfinding classification ({@code WalkNodeEvaluator.getPathTypeFromState}) for every
     * block state, as the ordinal of its {@code PathType} (same order as {@code path_type_malus.json}). The classifier
     * only reads the state at the queried position, so a block getter that answers with one state everywhere reproduces
     * it exactly; neighbour and above-block rules are applied by the consumer, which reads these same states.
     */
    private void dumpPathTypeStates() throws IOException {
        var classify = Vanilla.method(WALK_NODE, "getPathTypeFromState", BLOCK_GETTER, BLOCK_POS);
        var getBlockState = Vanilla.method(BLOCK_GETTER, "getBlockState", BLOCK_POS);
        var current = new Object[1];
        var getter = java.lang.reflect.Proxy.newProxyInstance(
            getClass().getClassLoader(),
            new Class<?>[] {Vanilla.type(BLOCK_GETTER)},
            (proxy, method, args) -> {
                if (method.getDeclaringClass() == Object.class) {
                    return switch (method.getName()) {
                        case "hashCode" -> System.identityHashCode(proxy);
                        case "equals" -> proxy == args[0];
                        default -> "BlockGetter";
                    };
                }
                if (method.getName().equals(getBlockState.getName()) && method.getParameterCount() == 1) {
                    return current[0];
                }
                throw new UnsupportedOperationException("Path type classification read " + method);
            });
        var types = (Object[]) Vanilla.call(Vanilla.method(PATH_TYPE, "values"), null);
        var result = new ArrayList<Integer>(states.size());
        for (var state : states) {
            current[0] = state;
            var type = Vanilla.call(classify, null, getter, origin);
            var ordinal = Arrays.asList(types).indexOf(type);
            if (ordinal < 0) {
                throw new IllegalStateException("Unknown path type for state " + state);
            }
            result.add(ordinal);
        }

        Json.write(output.resolve("path_type_states.json"), result, false);
    }

    /**
     * {@code tnt_blocks.json}: the blocks that are instances of {@code TntBlock}, whose static {@code explode}
     * FireBlock
     * calls on the block it replaces. Keyed by block, with the value {@code true}.
     */
    private void dumpTntBlocks() throws IOException {
        var tnt = Vanilla.type("net.minecraft.world.level.block.TntBlock");
        var blocks = new TreeMap<String, Boolean>();

        for (var block : (Iterable<?>) blockRegistry) {
            if (tnt.isInstance(block)) {
                blocks.put(key(blockRegistry, block), true);
            }
        }

        Json.write(output.resolve("tnt_blocks.json"), blocks, false);
    }

    /**
     * {@code sapling_growers.json}: the {@code TreeGrower} each sapling block holds in its {@code treeGrower} field
     * (declared by {@code SaplingBlock}, which the mangrove propagule extends), in the shape Obsidian's
     * {@code TreeGrower} still has: the configured features by identifier and one secondary chance. Which class owns
     * each sapling's random tick is in {@code block_random_tick_classes.json}.
     *
     * <p>Since 26.1 vanilla picks from weighted lists ({@code trees}, {@code megaTrees}, {@code flowerTrees}) instead.
     * A list's first entry is the feature ({@code tree}, {@code megaTree}, {@code flowers}), its second the secondary
     * one, and the second's share of the weight the secondary chance, which gives 1.21.11's values for every sapling it
     * had. A grower that this shape can't hold (more than two entries in a list, or lists with different chances, like
     * the poplar's three trees) is left out, so the sapling doesn't grow. Temporary until the gameplay port (Phase 6 of
     * {@code docs/porting-26.3-plan.md}) moves {@code TreeGrower} to weighted lists.
     */
    private void dumpSaplingGrowers() throws IOException {
        var sapling = Vanilla.type("net.minecraft.world.level.block.SaplingBlock");
        var growerField = Vanilla.field("net.minecraft.world.level.block.SaplingBlock", "treeGrower");
        var grower = "net.minecraft.world.level.block.grower.TreeGrower";
        var identifier = Vanilla.method("net.minecraft.resources.ResourceKey", "identifier");
        var unwrap = Vanilla.method("net.minecraft.util.random.WeightedList", "unwrap");
        var value = Vanilla.method("net.minecraft.util.random.Weighted", "value");
        var weight = Vanilla.method("net.minecraft.util.random.Weighted", "weight");
        // Each weighted list and the fields its first and second entries become.
        var lists = Map.of(
            "trees",
            new String[] {"tree", "secondaryTree"},
            "megaTrees",
            new String[] {"megaTree", "secondaryMegaTree"},
            "flowerTrees",
            new String[] {"flowers", "secondaryFlowers"});

        var blockRegistry = Vanilla.get(REGISTRIES, "BLOCK");
        var growers = new TreeMap<String, Map<String, Object>>();
        var skipped = new ArrayList<String>();

        for (var block : (Iterable<?>) blockRegistry) {
            if (!sapling.isInstance(block)) {
                continue;
            }

            var holder = Vanilla.get(growerField, block);
            var row = new TreeMap<String, Object>();
            Float chance = null;
            var fits = true;
            for (var list : lists.entrySet()) {
                var entries = (List<?>) Vanilla.call(unwrap, Vanilla.get(Vanilla.field(grower, list.getKey()), holder));
                if (entries.size() > 2) {
                    fits = false;
                    break;
                }
                for (var index = 0; index < entries.size(); index++) {
                    row.put(
                        list.getValue()[index],
                        Vanilla.call(identifier, Vanilla.call(value, entries.get(index))).toString());
                }
                if (entries.size() == 2) {
                    var first = (int) Vanilla.call(weight, entries.get(0));
                    var second = (int) Vanilla.call(weight, entries.get(1));
                    var listChance = (float) second / (first + second);
                    fits &= chance == null || chance == listChance;
                    chance = listChance;
                }
            }

            if (!fits) {
                skipped.add(key(blockRegistry, block));
                continue;
            }
            row.put("secondaryChance", chance == null ? 0f : chance);
            growers.put(key(blockRegistry, block), row);
        }

        if (!skipped.isEmpty()) {
            System.out.println(
                "These saplings' tree growers don't fit Obsidian's TreeGrower, so they don't grow: "
                + String.join(", ", skipped));
        }
        Json.write(output.resolve("sapling_growers.json"), growers, false);
    }

    private void dumpBlockOverrideClasses(String file, String name, String... parameterTypes) throws IOException {
        // The base no-op is declared by BlockBehaviour.
        var behaviour = "net.minecraft.world.level.block.state.BlockBehaviour";
        var baseMethod = Vanilla.method(behaviour, name, parameterTypes);
        var parameters = new Class<?>[parameterTypes.length];
        for (var i = 0; i < parameters.length; i++) {
            parameters[i] = Vanilla.type(parameterTypes[i]);
        }
        var classes = new TreeMap<String, String>();

        for (var block : (Iterable<?>) blockRegistry) {
            var owner = block.getClass();
            while (owner != null) {
                try {
                    owner.getDeclaredMethod(baseMethod.getName(), parameters);
                    break;
                } catch (NoSuchMethodException ignored) {
                    owner = owner.getSuperclass();
                }
            }

            if (owner != null && owner != Vanilla.type(behaviour)) {
                classes.put(key(blockRegistry, block), owner.getName());
            }
        }

        Json.write(output.resolve(file), classes, false);
    }

    /**
     * {@code item_use_on_classes.json}: the vanilla class that supplies each item's {@code useOn} override. Items
     * inheriting the base implementation are omitted.
     */
    private void dumpItemUseOnClasses() throws IOException {
        var item = "net.minecraft.world.item.Item";
        var context = "net.minecraft.world.item.context.UseOnContext";
        var itemRegistry = Vanilla.get(REGISTRIES, "ITEM");
        var baseMethod = Vanilla.method(item, "useOn", context);
        var parameter = Vanilla.type(context);
        var classes = new TreeMap<String, String>();

        for (var entry : (Iterable<?>) itemRegistry) {
            var owner = entry.getClass();
            while (owner != null) {
                try {
                    owner.getDeclaredMethod(baseMethod.getName(), parameter);
                    break;
                } catch (NoSuchMethodException ignored) {
                    owner = owner.getSuperclass();
                }
            }

            if (owner != null && owner != Vanilla.type(item)) {
                classes.put(key(itemRegistry, entry), owner.getName());
            }
        }

        Json.write(output.resolve("item_use_on_classes.json"), classes, false);
    }

    /**
     * {@code item_use_classes.json}: the vanilla class that supplies each item's {@code use} override (what a right
     * click does with the item in hand, like throwing an egg). Items inheriting the base implementation are omitted.
     */
    private void dumpItemUseClasses() throws IOException {
        var item = "net.minecraft.world.item.Item";
        var level = "net.minecraft.world.level.Level";
        var player = "net.minecraft.world.entity.player.Player";
        var hand = "net.minecraft.world.InteractionHand";
        var itemRegistry = Vanilla.get(REGISTRIES, "ITEM");
        var baseMethod = Vanilla.method(item, "use", level, player, hand);
        var parameters = baseMethod.getParameterTypes();
        var classes = new TreeMap<String, String>();

        for (var entry : (Iterable<?>) itemRegistry) {
            var owner = entry.getClass();
            while (owner != null) {
                try {
                    owner.getDeclaredMethod(baseMethod.getName(), parameters);
                    break;
                } catch (NoSuchMethodException ignored) {
                    owner = owner.getSuperclass();
                }
            }

            if (owner != null && owner != Vanilla.type(item)) {
                classes.put(key(itemRegistry, entry), owner.getName());
            }
        }

        Json.write(output.resolve("item_use_classes.json"), classes, false);
    }

    /**
     * {@code item_stacked_on_classes.json}, {@code item_other_stacked_on_classes.json} and
     * {@code item_container_fit_classes.json}: the vanilla class that supplies each item's
     * {@code overrideStackedOnOther}, {@code overrideOtherStackedOnMe} and {@code canFitInsideContainerItems}
     * overrides, which the click path and bundles consult. Items inheriting the base implementation are omitted.
     */
    private void dumpItemClickClasses() throws IOException {
        var item = "net.minecraft.world.item.Item";
        var stack = "net.minecraft.world.item.ItemStack";
        var slot = "net.minecraft.world.inventory.Slot";
        var action = "net.minecraft.world.inventory.ClickAction";
        var player = "net.minecraft.world.entity.player.Player";
        var access = "net.minecraft.world.entity.SlotAccess";
        var itemRegistry = Vanilla.get(REGISTRIES, "ITEM");

        writeItemOwners(
            "item_stacked_on_classes.json",
            itemRegistry,
            Vanilla.method(item, "overrideStackedOnOther", stack, slot, action, player));
        writeItemOwners(
            "item_other_stacked_on_classes.json",
            itemRegistry,
            Vanilla.method(item, "overrideOtherStackedOnMe", stack, stack, slot, action, player, access));
        writeItemOwners(
            "item_container_fit_classes.json",
            itemRegistry,
            Vanilla.method(item, "canFitInsideContainerItems"));
        writeBlockItemBlocks(itemRegistry);
    }

    /**
     * {@code item_container_fit_blocks.json}: the vanilla class of the block each {@code BlockItem} that doesn't fit
     * in container items places, which decides vanilla's {@code BlockItem.canFitInsideContainerItems} override.
     */
    private void writeBlockItemBlocks(Object itemRegistry) throws IOException {
        var blockItem = Vanilla.type("net.minecraft.world.item.BlockItem");
        var getBlock = Vanilla.method("net.minecraft.world.item.BlockItem", "getBlock");
        var blocks = new TreeMap<String, String>();

        for (var entry : (Iterable<?>) itemRegistry) {
            if (!blockItem.isInstance(entry)) {
                continue;
            }

            var block = Vanilla.call(getBlock, entry);
            blocks.put(key(itemRegistry, entry), block.getClass().getName());
        }

        Json.write(output.resolve("item_block_classes.json"), blocks, false);
    }

    private void writeItemOwners(String file, Object itemRegistry, java.lang.reflect.Method baseMethod)
        throws IOException {
        var parameters = baseMethod.getParameterTypes();
        var classes = new TreeMap<String, String>();
        var base = Vanilla.type("net.minecraft.world.item.Item");

        for (var entry : (Iterable<?>) itemRegistry) {
            var owner = entry.getClass();
            while (owner != null) {
                try {
                    owner.getDeclaredMethod(baseMethod.getName(), parameters);
                    break;
                } catch (NoSuchMethodException ignored) {
                    owner = owner.getSuperclass();
                }
            }

            if (owner != null && owner != base) {
                classes.put(key(itemRegistry, entry), owner.getName());
            }
        }

        Json.write(output.resolve(file), classes, false);
    }

    /**
     * {@code item_placed_blocks.json}: the block each {@code BlockItem} places by {@code BlockItem.getBlock()}, for
     * the items that place a block named differently from themselves (wheat seeds place wheat).
     */
    private void dumpItemPlacedBlocks() throws IOException {
        var blockItem = Vanilla.type("net.minecraft.world.item.BlockItem");
        var getBlock = Vanilla.method("net.minecraft.world.item.BlockItem", "getBlock");
        var itemRegistry = Vanilla.get(REGISTRIES, "ITEM");
        var blockRegistry = Vanilla.get(REGISTRIES, "BLOCK");
        var placed = new TreeMap<String, String>();

        for (var entry : (Iterable<?>) itemRegistry) {
            if (!blockItem.isInstance(entry)) {
                continue;
            }

            var itemKey = key(itemRegistry, entry);
            var blockKey = key(blockRegistry, Vanilla.call(getBlock, entry));
            if (!itemKey.equals(blockKey)) {
                placed.put(itemKey, blockKey);
            }
        }

        Json.write(output.resolve("item_placed_blocks.json"), placed, false);
    }

    /**
     * {@code item_attachments.json}: the blocks a {@code StandingAndWallBlockItem} places, as its fields hold them: the
     * standing block, the wall block, and the direction the standing block is placed against (the item places the
     * wall block against the other horizontal directions). The owner is the class declaring {@code getPlacementState}.
     */
    private void dumpItemAttachments() throws IOException {
        var standingAndWall = "net.minecraft.world.item.StandingAndWallBlockItem";
        var context = "net.minecraft.world.item.context.BlockPlaceContext";
        var itemRegistry = Vanilla.get(REGISTRIES, "ITEM");
        var blockRegistry = Vanilla.get(REGISTRIES, "BLOCK");
        var placement = Vanilla.method(standingAndWall, "getPlacementState", context);
        var parameter = Vanilla.type(context);
        var getBlock = Vanilla.method("net.minecraft.world.item.BlockItem", "getBlock");
        var wallBlock = Vanilla.field(standingAndWall, "wallBlock");
        var attachment = Vanilla.field(standingAndWall, "attachmentDirection");
        var attachments = new TreeMap<String, Map<String, String>>();

        for (var entry : (Iterable<?>) itemRegistry) {
            if (!Vanilla.type(standingAndWall).isInstance(entry)) {
                continue;
            }

            var owner = entry.getClass();
            while (owner != null) {
                try {
                    owner.getDeclaredMethod(placement.getName(), parameter);
                    break;
                } catch (NoSuchMethodException ignored) {
                    owner = owner.getSuperclass();
                }
            }

            var direction = (Enum<?>) Vanilla.get(attachment, entry);
            var row = new TreeMap<String, String>();
            row.put("owner", owner.getName());
            row.put("standing", key(blockRegistry, Vanilla.call(getBlock, entry)));
            row.put("wall", key(blockRegistry, Vanilla.get(wallBlock, entry)));
            row.put("direction", direction.name().toLowerCase(java.util.Locale.ROOT));
            attachments.put(key(itemRegistry, entry), row);
        }

        Json.write(output.resolve("item_attachments.json"), attachments, false);
    }

    /** The simple name of each block's class, sorted by block id. */
    private Map<String, String> blockClasses() {
        var classes = new TreeMap<String, String>();
        for (var block : (Iterable<?>) blockRegistry) {
            classes.put(key(blockRegistry, block), Vanilla.simpleName(block.getClass()));
        }
        return classes;
    }

    /**
     * The type of the block entity each block creates ({@code EntityBlock.newBlockEntity}), in block registry order.
     * Moving pistons create theirs elsewhere, so they have none here.
     */
    private Map<String, String> blockEntityTypes() {
        var entityBlock = Vanilla.type("net.minecraft.world.level.block.EntityBlock");
        var newBlockEntity =
            Vanilla.method("net.minecraft.world.level.block.EntityBlock", "newBlockEntity", BLOCK_POS, BLOCK_STATE);
        var getType = Vanilla.method("net.minecraft.world.level.block.entity.BlockEntity", "getType");
        var isValidBlock =
            Vanilla.method("net.minecraft.world.level.block.entity.BlockEntityType", "isValid", BLOCK_STATE);
        var defaultState = Vanilla.method(BLOCK, "defaultBlockState");
        var types = Vanilla.get(REGISTRIES, "BLOCK_ENTITY_TYPE");

        var result = new LinkedHashMap<String, String>();
        for (var block : (Iterable<?>) blockRegistry) {
            if (!entityBlock.isInstance(block)) {
                continue;
            }
            var blockEntity = Vanilla.call(newBlockEntity, block, origin, Vanilla.call(defaultState, block));
            if (blockEntity != null) {
                result.put(key(blockRegistry, block), key(types, Vanilla.call(getType, blockEntity)));
            } else {
                // A block whose newBlockEntity is null (MovingPistonBlock) gets its entity from elsewhere; vanilla's
                // BlockEntityType.isValid says which type that entity belongs to. Its ticks need the same type id.
                for (var type : (Iterable<?>) types) {
                    if ((boolean) Vanilla.call(isValidBlock, type, Vanilla.call(defaultState, block))) {
                        result.put(key(blockRegistry, block), key(types, type));
                    }
                }
            }
        }

        return result;
    }

    /** The network ids of the block entity types blocks create, in registry order. */
    private Map<String, Integer> blockEntityTypeIds(Map<String, String> blockEntityTypes) {
        var types = Vanilla.get(REGISTRIES, "BLOCK_ENTITY_TYPE");
        var getId = Vanilla.method("net.minecraft.core.IdMap", "getId", "java.lang.Object");
        var result = new LinkedHashMap<String, Integer>();
        for (var type : (Iterable<?>) types) {
            var key = key(types, type);
            if (blockEntityTypes.containsValue(key)) {
                result.put(key, (int) Vanilla.call(getId, types, type));
            }
        }

        return result;
    }

    /** The blocks whose default state {@code isSignalSource()}, sorted. */
    private List<String> signalSources() {
        var isSignalSource = Vanilla.method(STATE_BASE, "isSignalSource");
        var defaultState = Vanilla.method(BLOCK, "defaultBlockState");
        var result = new ArrayList<String>();
        for (var block : (Iterable<?>) blockRegistry) {
            if ((boolean) Vanilla.call(isSignalSource, Vanilla.call(defaultState, block))) {
                result.add(key(blockRegistry, block));
            }
        }

        result.sort(null);
        return result;
    }

    /**
     * {@code block_transforms.json}, read by {@code Obsidian/Registries/BlockTransforms.cs}: per transform, each
     * state's
     * {@code BlockState.rotate}/{@code mirror} result as a state id offset.
     */
    private void dumpBlockTransforms() throws IOException {
        var stateIds = Vanilla.get(BLOCK, "BLOCK_STATE_REGISTRY");
        var getId = Vanilla.method("net.minecraft.core.IdMapper", "getId", "java.lang.Object");
        var rotate = Vanilla.method(STATE_BASE, "rotate", "net.minecraft.world.level.block.Rotation");
        var mirror = Vanilla.method(STATE_BASE, "mirror", "net.minecraft.world.level.block.Mirror");
        var rotation = "net.minecraft.world.level.block.Rotation";
        var mirrors = "net.minecraft.world.level.block.Mirror";

        var json = new LinkedHashMap<String, Object>();
        json.put(
            "_notes",
            "Vanilla " + version + " BlockState.rotate/mirror results per state id, stored as (result - state id).");
        json.put("rotateClockwise90", transformed(stateIds, getId, rotate, Vanilla.get(rotation, "CLOCKWISE_90")));
        json.put("rotate180", transformed(stateIds, getId, rotate, Vanilla.get(rotation, "CLOCKWISE_180")));
        json.put(
            "rotateCounterclockwise90",
            transformed(stateIds, getId, rotate, Vanilla.get(rotation, "COUNTERCLOCKWISE_90")));
        json.put("mirrorLeftRight", transformed(stateIds, getId, mirror, Vanilla.get(mirrors, "LEFT_RIGHT")));
        json.put("mirrorFrontBack", transformed(stateIds, getId, mirror, Vanilla.get(mirrors, "FRONT_BACK")));
        Json.write(output.resolve("block_transforms.json"), json, false);
    }

    private int[] transformed(Object stateIds, Method getId, Method transform, Object argument) {
        var deltas = new int[states.size()];
        for (var id = 0; id < states.size(); id++) {
            deltas[id] = (int) Vanilla.call(getId, stateIds, Vanilla.call(transform, states.get(id), argument)) - id;
        }
        return deltas;
    }

    /**
     * {@code map_colors.bin}, read by {@code Obsidian/WorldData/Maps/MapColors.cs}: one byte per state, the id of
     * {@code BlockState.getMapColor}.
     */
    private void dumpMapColors() throws IOException {
        var getMapColor = Vanilla.method(STATE_BASE, "getMapColor", BLOCK_GETTER, BLOCK_POS);
        var colorId = Vanilla.field("net.minecraft.world.level.material.MapColor", "id");
        var colors = new byte[states.size()];
        for (var id = 0; id < states.size(); id++) {
            colors[id] = (byte) Vanilla.getInt(colorId, Vanilla.call(getMapColor, states.get(id), emptyLevel, origin));
        }
        Files.write(output.resolve("map_colors.bin"), colors);
    }

    /**
     * {@code wall_shape_covers.bin}, read by {@code Obsidian/WorldData/Features/Tree/WallShapeCovers.cs}: one byte per
     * state, which of {@code WallBlock}'s test shapes the bottom face of the state's collision shape covers
     * ({@code WallBlock.isCovered}, as {@code WallBlock.updateShape} checks the block above a wall).
     */
    private void dumpWallShapeCovers() throws IOException {
        var wall = "net.minecraft.world.level.block.WallBlock";
        var isCovered = Vanilla.method(wall, "isCovered", VOXEL_SHAPE, VOXEL_SHAPE);
        var getFaceShape = Vanilla.method(VOXEL_SHAPE, "getFaceShape", DIRECTION);
        var post = Vanilla.get(wall, "TEST_SHAPE_POST");
        var sides = (Map<?, ?>) Vanilla.get(wall, "TEST_SHAPES_WALL");
        // WallShapeCovers' bits after the post: north, east, south, west.
        var sideTests = new Object[] {
            sides.get(Vanilla.get(DIRECTION, "NORTH")),
            sides.get(Vanilla.get(DIRECTION, "EAST")),
            sides.get(Vanilla.get(DIRECTION, "SOUTH")),
            sides.get(Vanilla.get(DIRECTION, "WEST"))};
        var down = Vanilla.get(DIRECTION, "DOWN");

        var covers = new byte[states.size()];
        for (var id = 0; id < states.size(); id++) {
            var bottom =
                Vanilla.call(getFaceShape, Vanilla.call(getCollisionShape, states.get(id), emptyLevel, origin), down);
            var bits = bit(0, Vanilla.call(isCovered, null, bottom, post));
            for (var side = 0; side < 4; side++) {
                bits |= bit(1 + side, Vanilla.call(isCovered, null, bottom, sideTests[side]));
            }
            covers[id] = (byte) bits;
        }

        Files.write(output.resolve("wall_shape_covers.bin"), covers);
    }

    /** Creative tab metadata and item IDs, evaluated with the default feature flags and vanilla registry tags. */
    private void dumpCreativeTabs() throws IOException {
        // Creative variants consult dynamic registry tags (paintings, instruments, enchantments). Load the same
        // vanilla pack registries as a server, instead of datagen's unbound bootstrap holders.
        var provider = "net.minecraft.core.HolderLookup$Provider";
        var packType = "net.minecraft.server.packs.PackType";
        var vanillaPack = Vanilla.call(
            Vanilla.method("net.minecraft.server.packs.VanillaPackResources", "fullResources"),
            Vanilla.call(
                Vanilla.method("net.minecraft.server.packs.repository.ServerPacksSource", "createVanillaPackSource"),
                null));
        var resources = Vanilla.create(
            Vanilla.constructor(
                "net.minecraft.server.packs.resources.MultiPackResourceManager",
                packType,
                "java.util.List"),
            Vanilla.get(packType, "SERVER_DATA"),
            List.of(vanillaPack));
        var builtins = Vanilla.call(
            Vanilla.method("net.minecraft.core.RegistryAccess", "fromRegistryOfRegistries", REGISTRY),
            null,
            Vanilla.get(REGISTRIES, "REGISTRY"));
        var listRegistries = Vanilla.method(provider, "listRegistries");
        var builtinList = ((java.util.stream.Stream<?>) Vanilla.call(listRegistries, builtins)).toList();
        var loaded = ((java.util.concurrent.CompletableFuture<?>) Vanilla.call(
                          Vanilla.method(
                              "net.minecraft.resources.RegistryDataLoader",
                              "load",
                              "net.minecraft.server.packs.resources.ResourceManager",
                              "java.util.List",
                              "java.util.List",
                              "java.util.concurrent.Executor"),
                          null,
                          resources,
                          builtinList,
                          Vanilla.get("net.minecraft.resources.RegistryDataLoader", "WORLD_REGISTRIES"),
                          (java.util.concurrent.Executor) Runnable::run))
                         .join();
        var lookup = Vanilla.call(
            Vanilla.method(provider, "create", "java.util.stream.Stream"),
            null,
            java.util.stream.Stream.concat(
                builtinList.stream(),
                (java.util.stream.Stream<?>) Vanilla.call(listRegistries, loaded)));
        var flags = Vanilla.get("net.minecraft.world.flag.FeatureFlags", "DEFAULT_FLAGS");
        Vanilla.call(
            Vanilla.method(
                "net.minecraft.world.item.CreativeModeTabs",
                "tryRebuildTabContents",
                "net.minecraft.world.flag.FeatureFlagSet",
                "boolean",
                "net.minecraft.core.HolderLookup$Provider"),
            null,
            flags,
            false,
            lookup);
        var tabType = "net.minecraft.world.item.CreativeModeTab";
        var stackType = "net.minecraft.world.item.ItemStack";
        var itemRegistry = Vanilla.get(REGISTRIES, "ITEM");
        var tabRegistry = Vanilla.get(REGISTRIES, "CREATIVE_MODE_TAB");
        var getItem = Vanilla.method(stackType, "getItem");
        var getId = Vanilla.method(REGISTRY, "getId", "java.lang.Object");
        var tabs = new ArrayList<Object>();
        for (var tab : (Iterable<?>) tabRegistry) {
            var entry = new LinkedHashMap<String, Object>();
            entry.put("name", Vanilla.call(registryKey, tabRegistry, tab).toString());
            entry.put("row", ((Enum<?>) Vanilla.call(Vanilla.method(tabType, "row"), tab)).ordinal());
            entry.put("column", Vanilla.call(Vanilla.method(tabType, "column"), tab));
            entry.put("alignedRight", Vanilla.call(Vanilla.method(tabType, "isAlignedRight"), tab));
            var title = Vanilla.call(Vanilla.method(tabType, "getDisplayName"), tab);
            var contents = Vanilla.call(Vanilla.method("net.minecraft.network.chat.Component", "getContents"), title);
            entry.put(
                "title",
                Vanilla.call(
                    Vanilla.method("net.minecraft.network.chat.contents.TranslatableContents", "getKey"),
                    contents));
            var icon = Vanilla.call(Vanilla.method(tabType, "getIconItem"), tab);
            entry.put("icon", Vanilla.call(getId, itemRegistry, Vanilla.call(getItem, icon)));
            entry.put("background", Vanilla.call(Vanilla.method(tabType, "getBackgroundTexture"), tab).toString());
            // Preserve vanilla ordering and duplicates: component variants occupy distinct creative entries.
            // The client currently offers plain stacks; retaining every entry lets the component codec be added later.
            var items = new ArrayList<Integer>();
            for (var stack : (Iterable<?>) Vanilla.call(Vanilla.method(tabType, "getDisplayItems"), tab)) {
                items.add((int) Vanilla.call(getId, itemRegistry, Vanilla.call(getItem, stack)));
            }
            entry.put("items", items);
            tabs.add(entry);
        }
        Json.write(output.resolve("creative_tabs.json"), Map.of("version", version, "tabs", tabs), true);
    }

    /** Statistics registry ids, value registries and custom formatters for the client statistics screen. */
    private void dumpStatistics() throws IOException {
        var types = Vanilla.get(REGISTRIES, "STAT_TYPE");
        var getRegistry = Vanilla.method("net.minecraft.stats.StatType", "getRegistry");
        var getStat = Vanilla.method("net.minecraft.stats.StatType", "get", "java.lang.Object");
        var formatterField = Vanilla.field("net.minecraft.stats.Stat", "formatter");
        var formatterNames = List.of("DEFAULT", "DIVIDE_BY_TEN", "DISTANCE", "TIME");
        var formatters =
            formatterNames.stream().map(name -> Vanilla.get("net.minecraft.stats.StatFormatter", name)).toList();
        var result = new ArrayList<Object>();
        for (var type : (Iterable<?>) types) {
            var registry = Vanilla.call(getRegistry, type);
            var values = new ArrayList<Object>();
            for (var value : (Iterable<?>) registry) {
                var entry = new LinkedHashMap<String, Object>();
                entry.put("name", key(registry, value));
                if (key(types, type).equals("minecraft:custom")) {
                    var formatter = Vanilla.get(formatterField, Vanilla.call(getStat, type, value));
                    entry.put("formatter", formatterNames.get(formatters.indexOf(formatter)));
                }
                values.add(entry);
            }
            result.add(Map.of("name", key(types, type), "values", values));
        }
        var itemRegistry = Vanilla.get(REGISTRIES, "ITEM");
        var getId = Vanilla.method(REGISTRY, "getId", "java.lang.Object");
        var asItem = Vanilla.method(BLOCK, "asItem");
        var blockItems = new ArrayList<Integer>();
        for (var block : (Iterable<?>) blockRegistry) {
            blockItems.add((int) Vanilla.call(getId, itemRegistry, Vanilla.call(asItem, block)));
        }
        Json.write(output.resolve("statistics.json"), Map.of("types", result, "blockItems", blockItems), true);
    }

    /**
     * {@code entities.json}, read by {@code Obsidian.SourceGenerators/Registry/EntityGenerator.cs}: every entity type
     * by id, with its {@code EntityType} properties (its mob category included), default attributes
     * ({@code DefaultAttributes}), class name, the synced data ids its class declares
     * ({@code EntityDataAccessor} fields) and its superclass. The abstract superclasses follow by class name, with
     * their own synced data and superclass.
     */
    private void dumpEntities() throws IOException {
        var types = Vanilla.get(REGISTRIES, "ENTITY_TYPE");
        var attributes = Vanilla.get(REGISTRIES, "ATTRIBUTE");
        var attributeIds = new LinkedHashMap<String, Object>();
        for (var attribute : (Iterable<?>) attributes) {
            attributeIds.put(
                key(attributes, attribute),
                Vanilla.call(
                    Vanilla.method("net.minecraft.core.IdMap", "getId", "java.lang.Object"),
                    attributes,
                    attribute));
        }
        Json.write(output.resolve("attribute_ids.json"), attributeIds, false);
        var getId = Vanilla.method("net.minecraft.core.IdMap", "getId", "java.lang.Object");
        var wrapAsHolder = Vanilla.method(REGISTRY, "wrapAsHolder", "java.lang.Object");
        var getDescriptionId = Vanilla.method(ENTITY_TYPE, "getDescriptionId");
        var canSerialize = Vanilla.method(ENTITY_TYPE, "canSerialize");
        var canSummon = Vanilla.method(ENTITY_TYPE, "canSummon");
        var fireImmune = Vanilla.method(ENTITY_TYPE, "fireImmune");
        var getDefaultLootTable = Vanilla.method(ENTITY_TYPE, "getDefaultLootTable");
        var getDimensions = Vanilla.method(ENTITY_TYPE, "getDimensions");
        var dimensions = "net.minecraft.world.entity.EntityDimensions";
        var width = Vanilla.method(dimensions, "width");
        var height = Vanilla.method(dimensions, "height");
        var fixed = Vanilla.method(dimensions, "fixed");
        var getCategory = Vanilla.method(ENTITY_TYPE, "getCategory");
        var categoryName = Vanilla.method("net.minecraft.world.entity.MobCategory", "getName");
        var lootTableId = Vanilla.method("net.minecraft.resources.ResourceKey", "identifier");
        var defaultAttributes = "net.minecraft.world.entity.ai.attributes.DefaultAttributes";
        var hasSupplier = Vanilla.method(defaultAttributes, "hasSupplier", ENTITY_TYPE);
        var getSupplier = Vanilla.method(defaultAttributes, "getSupplier", ENTITY_TYPE);
        var supplier = "net.minecraft.world.entity.ai.attributes.AttributeSupplier";
        var hasAttribute = Vanilla.method(supplier, "hasAttribute", HOLDER);
        var getBaseValue = Vanilla.method(supplier, "getBaseValue", HOLDER);

        var classes = entityClasses();
        var entityClass = Vanilla.type(ENTITY);
        var json = new LinkedHashMap<String, Object>();
        var parents = new ArrayList<Class<?>>();
        for (var type : (Iterable<?>) types) {
            var entity = new LinkedHashMap<String, Object>();
            entity.put("id", Vanilla.call(getId, types, type));
            entity.put("translation_key", Vanilla.call(getDescriptionId, type));
            entity.put("serializable", Vanilla.call(canSerialize, type));
            entity.put("summonable", Vanilla.call(canSummon, type));
            entity.put("is_fire_immune", Vanilla.call(fireImmune, type));
            var lootTable = (Optional<?>) Vanilla.call(getDefaultLootTable, type);
            entity.put("loot_table", lootTable.map(key -> Vanilla.call(lootTableId, key).toString()).orElse(null));
            var size = Vanilla.call(getDimensions, type);
            entity.put("width", Vanilla.call(width, size));
            entity.put("height", Vanilla.call(height, size));
            entity.put("size_fixed", Vanilla.call(fixed, size));
            entity.put("category", Vanilla.call(categoryName, Vanilla.call(getCategory, type)));

            if ((boolean) Vanilla.call(hasSupplier, null, type)) {
                var values = new LinkedHashMap<String, Object>();
                var defaults = Vanilla.call(getSupplier, null, type);
                for (var attribute : (Iterable<?>) attributes) {
                    var holder = Vanilla.call(wrapAsHolder, attributes, attribute);
                    if ((boolean) Vanilla.call(hasAttribute, defaults, holder)) {
                        values.put(key(attributes, attribute), Vanilla.call(getBaseValue, defaults, holder));
                    }
                }
                entity.put("attributes", values);
            }

            var typeClass = classes.get(type);
            putClass(entity, typeClass);
            json.put(key(types, type), entity);

            for (var parent = typeClass.getSuperclass(); entityClass.isAssignableFrom(parent);
                 parent = parent.getSuperclass()) {
                if (!parents.contains(parent)) {
                    parents.add(parent);
                }
            }
        }

        // Superclasses that are also some type's class are already listed under that type.
        parents.removeAll(classes.values());
        for (var parent : parents) {
            var entry = new LinkedHashMap<String, Object>();
            putClass(entry, parent);
            entry.remove("class");
            if (json.put(Vanilla.simpleName(parent), entry) != null) {
                throw new IllegalStateException("Two entity classes are named " + Vanilla.simpleName(parent));
            }
        }

        Json.write(output.resolve("entities.json"), json, true);
    }

    /** Adds an entity class's name, the synced data its class declares by field name and id, and its superclass. */
    private void putClass(Map<String, Object> entry, Class<?> type) {
        var accessor = Vanilla.type("net.minecraft.network.syncher.EntityDataAccessor");
        var accessorId = Vanilla.method("net.minecraft.network.syncher.EntityDataAccessor", "id");
        var data = new ArrayList<Map.Entry<String, Integer>>();
        for (var field : type.getDeclaredFields()) {
            if (Modifier.isStatic(field.getModifiers()) && field.getType() == accessor) {
                field.setAccessible(true);
                data.add(Map.entry(field.getName(), (int) Vanilla.call(accessorId, Vanilla.getStatic(field))));
            }
        }
        data.sort(Map.Entry.comparingByValue());

        entry.put("class", Vanilla.simpleName(type));
        if (!data.isEmpty()) {
            var meta = new LinkedHashMap<String, Integer>();
            data.forEach(item -> meta.put(item.getKey(), item.getValue()));
            entry.put("meta", meta);
        }
        if (type != Vanilla.type(ENTITY)) {
            entry.put("parent", Vanilla.simpleName(type.getSuperclass()));
        }
    }

    /**
     * Each entity type's class, from the type argument of its {@code EntityType<T>} constant in {@code EntityTypes}
     * (the factories are lambdas that don't name the class, and creating entities would need a level).
     */
    private Map<Object, Class<?>> entityClasses() {
        var entityType = Vanilla.type(ENTITY_TYPE);
        var classes = new HashMap<Object, Class<?>>();
        for (var field : Vanilla.type("net.minecraft.world.entity.EntityTypes").getDeclaredFields()) {
            if (Modifier.isStatic(field.getModifiers()) && field.getType() == entityType
                && field.getGenericType() instanceof ParameterizedType generic
                && generic.getActualTypeArguments()[0] instanceof Class<?> type) {
                field.setAccessible(true);
                classes.put(Vanilla.getStatic(field), type);
            }
        }

        return classes;
    }

    /**
     * {@code worldgen_groups.json} in the work directory: the vanilla class that declares each configured and placed
     * feature's key (e.g. {@code minecraft:oak} in {@code TreeFeatures}, {@code minecraft:oak_checked} in
     * {@code TreePlacements}), from the {@code ResourceKey} constants of the classes in vanilla's
     * {@code data.worldgen.features} and {@code data.worldgen.placement} packages, so features can be grouped like
     * vanilla's bootstrap classes.
     */
    private void dumpWorldgenGroups(Path work) throws IOException {
        var json = new LinkedHashMap<String, Object>();
        json.put("feature", featureGroups("FEATURE", "net.minecraft.data.worldgen.features"));
        json.put("placed_feature", featureGroups("PLACED_FEATURE", "net.minecraft.data.worldgen.placement"));
        Json.write(work.resolve("worldgen_groups.json"), json, true);
    }

    /** The keys of one registry ({@code Registries} field name) declared in a package's classes, by declaring class. */
    private Map<String, String> featureGroups(String registryName, String packageName) {
        var registry = Vanilla.get("net.minecraft.core.registries.Registries", registryName);
        var resourceKey = "net.minecraft.resources.ResourceKey";
        var keyType = Vanilla.type(resourceKey);
        var isFor = Vanilla.method(resourceKey, "isFor", resourceKey);
        var identifier = Vanilla.method(resourceKey, "identifier");
        var groups = new TreeMap<String, String>(); // String's natural order is ordinal
        for (var className : Vanilla.classesIn(packageName)) {
            var declaring = Vanilla.type(className);
            for (var field : declaring.getDeclaredFields()) {
                if (!Modifier.isStatic(field.getModifiers()) || field.getType() != keyType) {
                    continue;
                }
                var key = Vanilla.getStatic(field);
                if (!(boolean) Vanilla.call(isFor, key, registry)) {
                    continue;
                }

                var id = Vanilla.call(identifier, key).toString();
                var group = Vanilla.simpleName(declaring);
                var previous = groups.put(id, group);
                if (previous != null && !previous.equals(group)) {
                    throw new IllegalStateException(id + " is declared in both " + previous + " and " + group);
                }
            }
        }

        return groups;
    }

    /**
     * {@code packet_fields.json} in the work directory: per direction ({@code PacketFlow.id()}) and packet id, the
     * fields of the packet's class, which {@code DatagenAssets} adds to {@code packets.json}. A packet's class is the
     * type argument of its {@code PacketType<T>} constant in vanilla's {@code *PacketTypes} classes.
     *
     * <p>The fields are the class's instance fields (its superclasses' too), each with whether vanilla sends it and,
     * if so, its encoding and whether it's only sometimes written ({@code conditional}), written in a loop
     * ({@code repeated}) or written together with other fields in one value ({@code packed}), as {@link PacketFormats}
     * reads them from the packet's codec. Sent fields come first, in the order they're written; the rest follow in
     * declaration order.
     */
    private void dumpPacketFields(Path work) throws IOException {
        var formats = new PacketFormats();
        var unread = new ArrayList<String>();
        var packetType = Vanilla.type(PACKET_TYPE);
        var flow = Vanilla.method(PACKET_TYPE, "flow");
        var id = Vanilla.method(PACKET_TYPE, "id");
        var flowId = Vanilla.method("net.minecraft.network.protocol.PacketFlow", "id");
        var classes = new HashMap<String, Class<?>>();
        var json = new TreeMap<String, Map<String, Object>>();
        collectPacketFields(formats, unread, packetType, flow, id, flowId, classes, json);

        if (classes.isEmpty()) {
            throw new IllegalStateException("Found no packet types");
        }
        if (!unread.isEmpty()) {
            System.out.println(
                "Couldn't read how these packets are written, so their fields don't say whether they're sent: "
                + String.join(", ", unread));
        }

        Json.write(work.resolve("packet_fields.json"), json, true);
        dumpPacketTypes(formats);
    }

    private void collectPacketFields(
        PacketFormats formats,
        List<String> unread,
        Class<?> packetType,
        Method flow,
        Method id,
        Method flowId,
        Map<String, Class<?>> classes,
        Map<String, Map<String, Object>> json) {
        for (var className : Vanilla.classes(
                 name -> name.startsWith("net.minecraft.network.protocol.") && name.endsWith("PacketTypes"))) {
            for (var field : Vanilla.type(className).getDeclaredFields()) {
                if (!Modifier.isStatic(field.getModifiers()) || field.getType() != packetType
                    || !(field.getGenericType() instanceof ParameterizedType generic)) {
                    continue;
                }

                // Generic packets (BundleDelimiterPacket<T>) are listed by their class.
                var argument = generic.getActualTypeArguments()[0];
                var type = (Class<?>) (argument instanceof ParameterizedType parameterized ? parameterized.getRawType()
                                                                                           : argument);
                var packet = Vanilla.getStatic(field);
                var direction = (String) Vanilla.call(flowId, Vanilla.call(flow, packet));
                var key = direction + " " + Vanilla.call(id, packet);
                var previous = classes.put(key, type);
                if (previous != null && previous != type) {
                    throw new IllegalStateException(
                        key + " is both " + Vanilla.simpleName(previous) + " and " + Vanilla.simpleName(type));
                }

                var writes = formats.writes(type);
                if (writes == null && !unread.contains(Vanilla.simpleName(type))) {
                    unread.add(Vanilla.simpleName(type));
                }
                json.computeIfAbsent(direction, name -> new TreeMap<>())
                    .put(Vanilla.call(id, packet).toString(), packetFields(type, writes));
            }
        }
    }

    /**
     * {@code packet_types.json}, read by {@code Obsidian.SourceGenerators/Packets}: the vanilla records and enums the
     * packets write (their encodings refer to them as {@code {"kind": "type"}} and {@code {"kind": "enum"}}), by
     * name ({@code Outer.Inner}). A record lists its fields like a packet does; an enum its wire value per constant.
     */
    private void dumpPacketTypes(PacketFormats formats) throws IOException {
        var json = new LinkedHashMap<String, Object>();
        for (var entry : formats.types().entrySet()) {
            var definition = entry.getValue();
            var type = new LinkedHashMap<String, Object>();
            if (definition.values() != null) {
                type.put("kind", "enum");
                type.put("values", definition.values());
            } else {
                type.put("kind", "record");
                type.put("fields", packetFields(definition.type(), definition.fields()));
            }
            json.put(entry.getKey(), type);
        }

        Json.write(output.resolve("packet_types.json"), json, true);
    }

    /**
     * A packet class's fields by name and type ({@link #typeName}), with how they're written when known
     * ({@code writes} isn't null): sent fields in the order they're written, then the rest.
     */
    private List<Object> packetFields(Class<?> type, Map<String, PacketFormats.Write> writes) {
        var fields = instanceFields(type);
        if (writes == null) {
            return new ArrayList<>(fields);
        }

        for (var field : fields) {
            var write = writes.get((String) field.get("name"));
            field.put("sent", write != null);
            if (write != null) {
                field.put("encoding", write.encoding());
                if (write.conditional()) {
                    field.put("conditional", true);
                }
                if (write.repeated()) {
                    field.put("repeated", true);
                }
                if (write.packed()) {
                    field.put("packed", true);
                }
            }
        }
        var order = Comparator.comparingInt((Map<String, Object> field) -> {
            var write = writes.get((String) field.get("name"));
            return write == null ? Integer.MAX_VALUE : write.order();
        });
        return new ArrayList<>(fields.stream().sorted(order).toList());
    }

    /** A class's instance fields, its superclasses' first, by name and type. */
    private List<Map<String, Object>> instanceFields(Class<?> type) {
        var fields =
            type.getSuperclass() == null ? new ArrayList<Map<String, Object>>() : instanceFields(type.getSuperclass());
        for (var field : type.getDeclaredFields()) {
            if (Modifier.isStatic(field.getModifiers()) || field.isSynthetic()) {
                continue;
            }

            var entry = new LinkedHashMap<String, Object>();
            entry.put("name", field.getName());
            entry.put("type", typeName(field.getGenericType()));
            fields.add(entry);
        }

        return fields;
    }

    /**
     * A Java type by its names, without packages and with nested classes after their outer class:
     * {@code int}, {@code Optional<Component>}, {@code ClientboundBossEventPacket.Operation}.
     */
    private String typeName(Type type) {
        return switch (type) {
            case Class<?> array when array.isArray() -> typeName(array.getComponentType()) + "[]";
            case Class<?> plain -> Vanilla.nestedName(plain);
            case ParameterizedType generic ->
                typeName(generic.getRawType())
                    + Arrays.stream(generic.getActualTypeArguments())
                          .map(this::typeName)
                          .collect(Collectors.joining(", ", "<", ">"));
            case GenericArrayType array -> typeName(array.getGenericComponentType()) + "[]";
            case WildcardType wildcard when wildcard.getLowerBounds().length > 0 ->
                "? super " + typeName(wildcard.getLowerBounds()[0]);
            case WildcardType wildcard when wildcard.getUpperBounds()[0] == Object.class -> "?";
            case WildcardType wildcard -> "? extends " + typeName(wildcard.getUpperBounds()[0]);
            default -> type.getTypeName(); // type variables
        };
    }

    // Helpers.

    /** The id of a registry entry, like {@code minecraft:stone}. */
    private String key(Object registry, Object value) {
        return Vanilla.call(registryKey, registry, value).toString();
    }

    /** The boxes of a shape ({@code VoxelShape.toAabbs}), as min x, y, z then max x, y, z. */
    private List<double[]> boxes(Object shape) {
        var result = new ArrayList<double[]>();
        for (var box : (List<?>) Vanilla.call(toAabbs, shape)) {
            var values = new double[6];
            for (var i = 0; i < 6; i++) {
                values[i] = Vanilla.getDouble(boxBounds[i], box);
            }
            result.add(values);
        }

        return result;
    }

    // Directions by ordinal are down, up, north, south, west, east: pairs along y, z and x, negative first.

    /** The two axes (0 x, 1 y, 2 z) that span a direction's face, in x, y, z order. */
    private static int[] faceAxes(Object direction) {
        return switch (((Enum<?>) direction).ordinal() / 2) {
            case 0 -> new int[] {0, 2}; // down, up
            case 1 -> new int[] {0, 1}; // north, south
            default -> new int[] {1, 2}; // west, east
        };
    }

    /** Whether a direction points along its axis (up, south, east). */
    private static boolean isPositive(Object direction) {
        return ((Enum<?>) direction).ordinal() % 2 == 1;
    }

    private static int bit(int bit, Object value) {
        return (boolean) value ? 1 << bit : 0;
    }

    private static int indexOf(Object[] values, Object value) {
        for (var i = 0; i < values.length; i++) {
            if (values[i] == value) {
                return i;
            }
        }
        return 0;
    }

    /** Numbers distinct values in the order they're first seen. */
    private static final class Indexer<T> {
        private final Map<Object, Integer> indexes = new HashMap<>();

        private final List<T> values = new ArrayList<>();

        private final Function<T, Object> key;

        Indexer() {
            this(value -> value);
        }

        /** Numbers values by a key, for values without a usable equals (such as arrays). */
        Indexer(Function<T, Object> key) {
            this.key = key;
        }

        int indexOf(T value) {
            return indexes.computeIfAbsent(key.apply(value), k -> {
                values.add(value);
                return values.size() - 1;
            });
        }

        List<T> values() {
            return values;
        }
    }

    /** A minimal JSON writer for maps, lists, int arrays, strings, numbers, booleans and null. */
    static final class Json {
        static void write(Path path, Object value, boolean indented) throws IOException {
            var builder = new StringBuilder();
            append(builder, value, indented ? "\n" : null);
            if (indented) {
                builder.append('\n');
            }
            Files.writeString(path, builder, StandardCharsets.UTF_8);
        }

        // newline is null for compact output, else the line break plus the current indentation.
        private static void append(StringBuilder builder, Object value, String newline) {
            var inner = newline == null ? null : newline + "  ";
            if (value instanceof Map<?, ?> map) {
                builder.append('{');
                var first = true;
                for (var entry : map.entrySet()) {
                    builder.append(first ? "" : ",");
                    if (inner != null) {
                        builder.append(inner);
                    }
                    string(builder, entry.getKey().toString());
                    builder.append(inner != null ? ": " : ":");
                    append(builder, entry.getValue(), inner);
                    first = false;
                }
                if (newline != null && !map.isEmpty()) {
                    builder.append(newline);
                }
                builder.append('}');
            } else if (value instanceof int[] array) {
                builder.append('[');
                for (var i = 0; i < array.length; i++) {
                    builder.append(i == 0 ? "" : ",").append(array[i]);
                }
                builder.append(']');
            } else if (value instanceof double[] array) {
                builder.append('[');
                for (var i = 0; i < array.length; i++) {
                    builder.append(i == 0 ? "" : ",").append(array[i]);
                }
                builder.append(']');
            } else if (value instanceof List<?> list) {
                builder.append('[');
                for (var i = 0; i < list.size(); i++) {
                    builder.append(i == 0 ? "" : ",");
                    append(builder, list.get(i), null);
                }
                builder.append(']');
            } else if (value instanceof String text) {
                string(builder, text);
            } else {
                builder.append(value); // numbers, booleans, null
            }
        }

        private static void string(StringBuilder builder, String text) {
            builder.append('"');
            for (var c : text.toCharArray()) {
                if (c == '"' || c == '\\') {
                    builder.append('\\').append(c);
                } else if (c < 0x20) {
                    builder.append(String.format("\\u%04x", (int) c));
                } else {
                    builder.append(c);
                }
            }
            builder.append('"');
        }
    }
}
