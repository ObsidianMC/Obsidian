import java.io.IOException;
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
import java.lang.reflect.Constructor;
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
import java.util.regex.Matcher;
import java.util.regex.Pattern;
import java.util.stream.Collectors;

/**
 * Dumps the vanilla data Obsidian needs that Mojang's data generators don't report: per block state light, physics,
 * transforms, map colors and wall shape covers, entity data, and the fields of each packet.
 *
 * <p>Usage, with the unbundled server jar and its libraries on the class path:
 * {@code java -cp <server + libraries> VanillaDumper.java <server mappings (server.txt)> <assets directory> <work directory>}.
 * The assets go to the assets directory; {@code worldgen_groups.json} and {@code packet_fields.json}, which only the
 * generator itself uses, go to the work directory.
 *
 * <p>The server jar is obfuscated, so everything vanilla is reached through reflection, by its Mojang name looked up in
 * Mojang's ProGuard mappings (see {@link Mojang}). The values come from calling vanilla's own methods on every block
 * state or entity type, except how packets are written, which {@link PacketFormats} reads from their bytecode; the file
 * formats are Obsidian's, documented by their readers (named on each dump method).
 *
 * <p>Needs Java 25: the source launcher compiles {@code PacketFormats.java} next to this file, and it uses the
 * class-file API ({@code java.lang.classfile}).
 */
public final class VanillaDumper {
    // Mojang names of the classes used below, as the mappings spell them (also how parameter types are given).
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

    private final Mojang mojang;
    private final Path output;
    private final String version;

    // Shared arguments: vanilla's empty world (air everywhere) at the origin, for methods that take a level and position.
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
        if (args.length != 3) {
            System.err.println("Usage: java VanillaDumper.java <server mappings> <assets directory> <work directory>");
            System.exit(1);
        }

        var started = System.nanoTime();
        var mojang = Mojang.read(Path.of(args[0]));
        var output = Path.of(args[1]);
        var work = Path.of(args[2]);
        Files.createDirectories(output);
        Files.createDirectories(work);

        var version = bootstrap(mojang);
        var dumper = new VanillaDumper(mojang, output, version);
        dumper.dumpBlockLight();
        dumper.dumpBlockPhysics();
        dumper.dumpBlockTransforms();
        dumper.dumpMapColors();
        dumper.dumpWallShapeCovers();
        dumper.dumpEntities();
        dumper.dumpWorldgenGroups(work);
        dumper.dumpPacketFields(work);
        System.out.printf("Dumped vanilla data in %.1fs%n", (System.nanoTime() - started) / 1e9);
    }

    /**
     * Starts vanilla like its server and data generator do (version info first, then the registries), and binds the
     * built-in registries' tags from vanilla's data pack: some block methods check tags (e.g.
     * {@code BlockTags.SIGNS} in {@code FlowingFluid.canHoldAnyFluid}), and tags are only bound when data packs load.
     */
    private static String bootstrap(Mojang mojang) {
        var sharedConstants = "net.minecraft.SharedConstants";
        Mojang.call(mojang.method(sharedConstants, "tryDetectVersion"), null);
        Mojang.call(mojang.method("net.minecraft.server.Bootstrap", "bootStrap"), null);

        var packType = "net.minecraft.server.packs.PackType";
        var vanillaPack = Mojang.call(mojang.method("net.minecraft.server.packs.repository.ServerPacksSource", "createVanillaPackSource"), null);
        var resources = Mojang.create(mojang.constructor("net.minecraft.server.packs.resources.MultiPackResourceManager", packType, "java.util.List"),
            mojang.get(packType, "SERVER_DATA"), List.of(vanillaPack));

        var loadPendingTags = mojang.method("net.minecraft.tags.TagLoader", "loadPendingTags",
            "net.minecraft.server.packs.resources.ResourceManager", REGISTRY);
        var pendingTags = "net.minecraft.core.Registry$PendingTags";
        var size = mojang.method(pendingTags, "size");
        var apply = mojang.method(pendingTags, "apply");
        var bound = 0;
        for (var registry : (Iterable<?>) mojang.get(REGISTRIES, "REGISTRY")) {
            var pending = (Optional<?>) Mojang.call(loadPendingTags, null, resources, registry);
            if (pending.isPresent()) {
                bound += (int) Mojang.call(size, pending.get());
                Mojang.call(apply, pending.get());
            }
        }
        if (bound == 0)
            throw new IllegalStateException("Vanilla's data pack has no tags");

        var currentVersion = Mojang.call(mojang.method(sharedConstants, "getCurrentVersion"), null);
        return (String) Mojang.call(mojang.method("net.minecraft.WorldVersion", "name"), currentVersion);
    }

    private VanillaDumper(Mojang mojang, Path output, String version) {
        this.mojang = mojang;
        this.output = output;
        this.version = version;
        emptyLevel = mojang.get("net.minecraft.world.level.EmptyBlockGetter", "INSTANCE");
        origin = mojang.get(BLOCK_POS, "ZERO");
        directions = (Object[]) Mojang.call(mojang.method(DIRECTION, "values"), null);
        getBlock = mojang.method(STATE_BASE, "getBlock");
        getCollisionShape = mojang.method(STATE_BASE, "getCollisionShape", BLOCK_GETTER, BLOCK_POS);
        getFluidState = mojang.method(STATE_BASE, "getFluidState");
        registryKey = mojang.method(REGISTRY, "getKey", "java.lang.Object");
        blockRegistry = mojang.get(REGISTRIES, "BLOCK");
        emptyShape = Mojang.call(mojang.method(SHAPES, "empty"), null);
        blockShape = Mojang.call(mojang.method(SHAPES, "block"), null);

        getAxis = mojang.method(DIRECTION, "getAxis");
        shapeMin = mojang.method(VOXEL_SHAPE, "min", AXIS);
        shapeMax = mojang.method(VOXEL_SHAPE, "max", AXIS);
        toAabbs = mojang.method(VOXEL_SHAPE, "toAabbs");
        var aabb = "net.minecraft.world.phys.AABB";
        boxBounds = new Field[] { mojang.field(aabb, "minX"), mojang.field(aabb, "minY"), mojang.field(aabb, "minZ"),
            mojang.field(aabb, "maxX"), mojang.field(aabb, "maxY"), mojang.field(aabb, "maxZ") };
        discreteShape = mojang.field(VOXEL_SHAPE, "shape");
        discreteSize = mojang.method("net.minecraft.world.phys.shapes.DiscreteVoxelShape", "getSize", AXIS);
        sliceShape = mojang.constructor("net.minecraft.world.phys.shapes.SliceShape", VOXEL_SHAPE, AXIS, "int");

        // Block.BLOCK_STATE_REGISTRY holds every state by its id, which is what Obsidian's per-state tables index by.
        var stateIds = mojang.get(BLOCK, "BLOCK_STATE_REGISTRY");
        var byId = mojang.method("net.minecraft.core.IdMapper", "byId", "int");
        var size = (int) Mojang.call(mojang.method("net.minecraft.core.IdMapper", "size"), stateIds);
        for (var id = 0; id < size; id++)
            states.add(Mojang.call(byId, stateIds, id));
    }

    /**
     * {@code block_light.json}, read by {@code Obsidian/Registries/BlockLight.cs}: per state, {@code getLightBlock()}
     * plus a 1-based index into {@code shapes} for the states whose shape occludes light, each shape listing its six
     * face masks ({@code LightEngine.getOcclusionShape} per direction) as indexes into {@code faces}.
     */
    private void dumpBlockLight() throws IOException {
        var getLightBlock = mojang.method(STATE_BASE, "getLightBlock");
        var lightEngine = "net.minecraft.world.level.lighting.LightEngine";
        // States that don't use their shape for light occlusion have only empty occlusion shapes (isEmptyShape).
        var isEmptyShape = mojang.method(lightEngine, "isEmptyShape", BLOCK_STATE);
        var getOcclusionShape = mojang.method(lightEngine, "getOcclusionShape", BLOCK_STATE, DIRECTION);

        var light = new int[states.size()];
        var faces = new Indexer<String>();
        var shapes = new Indexer<List<Integer>>();
        var shapeStates = new ArrayList<Object>(); // one state per shape, to check the masks against vanilla below
        for (var id = 0; id < states.size(); id++) {
            var state = states.get(id);
            light[id] = (int) Mojang.call(getLightBlock, state);
            if ((boolean) Mojang.call(isEmptyShape, null, state))
                continue;

            var shapeFaces = new ArrayList<Integer>();
            for (var direction : directions)
                shapeFaces.add(faces.indexOf(faceMask(Mojang.call(getOcclusionShape, null, state, direction), direction)));

            var shape = shapes.indexOf(shapeFaces);
            if (shape == shapeStates.size())
                shapeStates.add(state);
            light[id] |= (shape + 1) << 4;
        }

        checkLightMasks(shapeStates, shapes.values(), faces.values());

        var json = new LinkedHashMap<String, Object>();
        json.put("_notes", "Vanilla " + version + " block state light properties, indexed by state id. See BlockLight for the layout.");
        json.put("light", light);
        json.put("shapes", shapes.values());
        json.put("faces", faces.values());
        Json.write(output.resolve("block_light.json"), json, false);
    }

    /**
     * A face shape as a 16x16 grid of 1/16 cells, one bit per cell whose center the shape covers, in hex. Rows run along
     * the first of the face's two axes in x, y, z order and columns along the second; row r is bits 16r to 16r+15 of the
     * 256-bit mask, written as four 64-bit words, lowest first.
     */
    private String faceMask(Object faceShape, Object direction) {
        var axes = faceAxes(direction);
        var words = new long[4];
        for (var box : boxes(faceShape)) {
            for (var row = 0; row < 16; row++) {
                var u = (row + 0.5) / 16;
                if (u < box[axes[0]] || u > box[axes[0] + 3])
                    continue;
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
        var getOcclusionShape = mojang.method("net.minecraft.world.level.lighting.LightEngine", "getOcclusionShape",
            BLOCK_STATE, DIRECTION);
        var faceShapeOccludes = mojang.method(SHAPES, "faceShapeOccludes", VOXEL_SHAPE, VOXEL_SHAPE);
        var masks = faces.stream().map(VanillaDumper::maskWords).toList();
        for (var from = 0; from < shapes.size(); from++) {
            for (var to = 0; to < shapes.size(); to++) {
                for (var direction = 0; direction < 6; direction++) {
                    var fromMask = masks.get(shapes.get(from).get(direction));
                    var toMask = masks.get(shapes.get(to).get(direction ^ 1)); // opposite directions differ in bit 0
                    var covered = true;
                    for (var word = 0; word < 4; word++)
                        covered &= (fromMask[word] | toMask[word]) == -1L;

                    var vanilla = (boolean) Mojang.call(faceShapeOccludes, null,
                        Mojang.call(getOcclusionShape, null, shapeStates.get(from), directions[direction]),
                        Mojang.call(getOcclusionShape, null, shapeStates.get(to), directions[direction ^ 1]));
                    if (covered != vanilla)
                        throw new IllegalStateException("Light face masks don't match LightEngine.shapeOccludes for shapes " + from + " and " + to);
                }
            }
        }
    }

    private static long[] maskWords(String hex) {
        var words = new long[4];
        for (var word = 0; word < 4; word++)
            words[word] = Long.parseUnsignedLong(hex.substring(word * 16, word * 16 + 16), 16);
        return words;
    }

    /**
     * {@code block_physics.json}, read by {@code Obsidian/Registries/BlockPhysics.cs}, which documents the bit layouts of
     * {@code flags} and {@code fluidFlags}. Each bit is the vanilla method named next to it below, called on the state in
     * the empty world at the origin.
     */
    private void dumpBlockPhysics() throws IOException {
        var isAir = mojang.method(STATE_BASE, "isAir");
        var blocksMotion = mojang.method(STATE_BASE, "blocksMotion");
        var isSolid = mojang.method(STATE_BASE, "isSolid");
        var canBeReplaced = mojang.method(STATE_BASE, "canBeReplaced");
        var liquid = mojang.method(STATE_BASE, "liquid");
        var isCollisionShapeFullBlock = mojang.method(STATE_BASE, "isCollisionShapeFullBlock", BLOCK_GETTER, BLOCK_POS);
        var isFaceSturdy = mojang.method(STATE_BASE, "isFaceSturdy", BLOCK_GETTER, BLOCK_POS, DIRECTION, SUPPORT_TYPE);
        var isRedstoneConductor = mojang.method(STATE_BASE, "isRedstoneConductor", BLOCK_GETTER, BLOCK_POS);
        var hasBlockEntity = mojang.method(STATE_BASE, "hasBlockEntity");
        var getLightEmission = mojang.method(STATE_BASE, "getLightEmission");
        var isSolidRender = mojang.method(STATE_BASE, "isSolidRender");
        var isSource = mojang.method(FLUID_STATE, "isSource");
        var getAmount = mojang.method(FLUID_STATE, "getAmount");
        var getType = mojang.method(FLUID_STATE, "getType");
        var isEmpty = mojang.method(VOXEL_SHAPE, "isEmpty");
        var isFaceFull = mojang.method(BLOCK, "isFaceFull", VOXEL_SHAPE, DIRECTION);
        var is = mojang.method(STATE_BASE, "is", TAG_KEY);
        var unstableBottomCenter = mojang.get("net.minecraft.tags.BlockTags", "UNSTABLE_BOTTOM_CENTER");
        var full = mojang.get(SUPPORT_TYPE, "FULL");
        var center = mojang.get(SUPPORT_TYPE, "CENTER");
        var rigid = mojang.get(SUPPORT_TYPE, "RIGID");
        var up = mojang.get(DIRECTION, "UP");
        var down = mojang.get(DIRECTION, "DOWN");

        var liquidBlockContainer = mojang.type("net.minecraft.world.level.block.LiquidBlockContainer");
        var canHoldAnyFluid = mojang.method(FLOWING_FLUID, "canHoldAnyFluid", BLOCK_STATE);
        var canHoldSpecificFluid = mojang.method(FLOWING_FLUID, "canHoldSpecificFluid", BLOCK_GETTER, BLOCK_POS, BLOCK_STATE, FLUID);
        // Obsidian's FluidKind: 1-4 are these fluids (0 is empty). Not vanilla's registry ids, which order flowing first.
        var fluids = new Object[] { null, mojang.get(FLUIDS, "WATER"), mojang.get(FLUIDS, "FLOWING_WATER"),
            mojang.get(FLUIDS, "LAVA"), mojang.get(FLUIDS, "FLOWING_LAVA") };

        var flags = new int[states.size()];
        var fluidFlags = new int[states.size()];
        var collisionShapeIds = new int[states.size()];
        var collisionShapes = new Indexer<List<double[]>>(shape -> shape.stream().map(Arrays::toString).toList());
        var faces = new Indexer<List<double[]>>(face -> face.stream().map(Arrays::toString).toList());
        var faceSets = new Indexer<List<Integer>>();
        faceSets.indexOf(List.of(0, 0, 0, 0, 0, 0)); // set 0: no faces, also used for full blocks (checked first)
        for (var id = 0; id < states.size(); id++) {
            var state = states.get(id);
            var fluidState = Mojang.call(getFluidState, state);
            var collision = Mojang.call(getCollisionShape, state, emptyLevel, origin);
            collisionShapeIds[id] = collisionShapes.indexOf(boxes(collision));

            var bits = 0;
            bits |= bit(0, Mojang.call(isAir, state));
            bits |= bit(1, Mojang.call(blocksMotion, state));
            bits |= bit(2, Mojang.call(isSolid, state));
            bits |= bit(3, Mojang.call(canBeReplaced, state));
            bits |= bit(4, Mojang.call(liquid, state));
            bits |= bit(5, Mojang.call(isSource, fluidState));
            bits |= bit(6, Mojang.call(isCollisionShapeFullBlock, state, emptyLevel, origin));
            bits |= bit(7, Mojang.call(isFaceSturdy, state, emptyLevel, origin, up, center));
            bits |= bit(8, Mojang.call(isFaceSturdy, state, emptyLevel, origin, up, rigid));
            bits |= bit(9, Mojang.call(isRedstoneConductor, state, emptyLevel, origin));
            bits |= bit(10, Mojang.call(hasBlockEntity, state));
            for (var face = 0; face < 6; face++)
                bits |= bit(11 + face, Mojang.call(isFaceSturdy, state, emptyLevel, origin, directions[face], full));
            bits |= (int) Mojang.call(getLightEmission, state) << 17;
            bits |= (int) Mojang.call(getAmount, fluidState) << 21;
            bits |= indexOf(fluids, Mojang.call(getType, fluidState)) << 25;
            bits |= bit(28, Mojang.call(isEmpty, collision));
            bits |= bit(29, Mojang.call(isFaceFull, null, collision, up));
            bits |= bit(30, Mojang.call(isSolidRender, state));
            // Block.canSupportCenter(level, pos, DOWN), which needs a LevelReader; its body is this check.
            var unstable = (boolean) Mojang.call(is, state, unstableBottomCenter);
            bits |= bit(31, !unstable && (boolean) Mojang.call(isFaceSturdy, state, emptyLevel, origin, down, center));
            flags[id] = bits;

            var fluidBits = 0;
            fluidBits |= bit(0, collision == blockShape);
            fluidBits |= bit(1, collision == emptyShape);
            fluidBits |= bit(2, liquidBlockContainer.isInstance(Mojang.call(getBlock, state)));
            fluidBits |= bit(3, Mojang.call(canHoldAnyFluid, null, state));
            for (var fluid = 1; fluid < fluids.length; fluid++)
                fluidBits |= bit(3 + fluid, Mojang.call(canHoldSpecificFluid, null, emptyLevel, origin, state, fluids[fluid]));

            if (collision != blockShape) {
                var set = new ArrayList<Integer>();
                for (var direction : directions)
                    set.add(faces.indexOf(collisionFace(collision, direction)));
                fluidBits |= faceSets.indexOf(set) << 16;
            }

            fluidFlags[id] = fluidBits;
        }

        var json = new LinkedHashMap<String, Object>();
        json.put("_notes", "Vanilla " + version + " block state physics, indexed by state id. See BlockPhysics for the bit layouts.");
        json.put("flags", flags);
        json.put("blockClasses", blockClasses());
        var blockEntityTypes = blockEntityTypes();
        json.put("blockEntityTypes", blockEntityTypes);
        json.put("blockEntityTypeIds", blockEntityTypeIds(blockEntityTypes));
        json.put("signalSources", signalSources());
        json.put("fluidFlags", fluidFlags);
        json.put("collisionFaceSets", faceSets.values());
        json.put("collisionFaces", faces.values());
        json.put("collisionShapeIds", collisionShapeIds);
        json.put("collisionShapes", collisionShapes.values());
        Json.write(output.resolve("block_physics.json"), json, false);
    }

    /**
     * Where a collision shape touches the given face of its block, as the boxes (min and max on the face's two axes)
     * of the slice that {@code Shapes.mergedFaceOccludes} takes: the shape's outermost layer of cells on that side, or
     * nothing when the shape doesn't reach the side.
     */
    private List<double[]> collisionFace(Object shape, Object direction) {
        var axis = Mojang.call(getAxis, direction);
        var positive = isPositive(direction);
        var edge = (double) Mojang.call(positive ? shapeMax : shapeMin, shape, axis);
        if (Math.abs(edge - (positive ? 1 : 0)) > 1.0E-7)
            return List.of();

        var size = (int) Mojang.call(discreteSize, Mojang.get(discreteShape, shape), axis);
        var slice = Mojang.create(sliceShape, shape, axis, positive ? size - 1 : 0);

        var axes = faceAxes(direction);
        var face = new ArrayList<double[]>();
        for (var box : boxes(slice))
            face.add(new double[] { box[axes[0]], box[axes[1]], box[axes[0] + 3], box[axes[1] + 3] });
        return face;
    }

    /** The Mojang simple name of each block's class, sorted by block id. */
    private Map<String, String> blockClasses() {
        var classes = new TreeMap<String, String>();
        for (var block : (Iterable<?>) blockRegistry)
            classes.put(key(blockRegistry, block), mojang.simpleName(block.getClass()));
        return classes;
    }

    /**
     * The type of the block entity each block creates ({@code EntityBlock.newBlockEntity}), in block registry order.
     * Moving pistons create theirs elsewhere, so they have none here.
     */
    private Map<String, String> blockEntityTypes() {
        var entityBlock = mojang.type("net.minecraft.world.level.block.EntityBlock");
        var newBlockEntity = mojang.method("net.minecraft.world.level.block.EntityBlock", "newBlockEntity", BLOCK_POS, BLOCK_STATE);
        var getType = mojang.method("net.minecraft.world.level.block.entity.BlockEntity", "getType");
        var defaultState = mojang.method(BLOCK, "defaultBlockState");
        var types = mojang.get(REGISTRIES, "BLOCK_ENTITY_TYPE");

        var result = new LinkedHashMap<String, String>();
        for (var block : (Iterable<?>) blockRegistry) {
            if (!entityBlock.isInstance(block))
                continue;
            var blockEntity = Mojang.call(newBlockEntity, block, origin, Mojang.call(defaultState, block));
            if (blockEntity != null)
                result.put(key(blockRegistry, block), key(types, Mojang.call(getType, blockEntity)));
        }

        return result;
    }

    /** The network ids of the block entity types blocks create, in registry order. */
    private Map<String, Integer> blockEntityTypeIds(Map<String, String> blockEntityTypes) {
        var types = mojang.get(REGISTRIES, "BLOCK_ENTITY_TYPE");
        var getId = mojang.method("net.minecraft.core.IdMap", "getId", "java.lang.Object");
        var result = new LinkedHashMap<String, Integer>();
        for (var type : (Iterable<?>) types) {
            var key = key(types, type);
            if (blockEntityTypes.containsValue(key))
                result.put(key, (int) Mojang.call(getId, types, type));
        }

        return result;
    }

    /** The blocks whose default state {@code isSignalSource()}, sorted. */
    private List<String> signalSources() {
        var isSignalSource = mojang.method(STATE_BASE, "isSignalSource");
        var defaultState = mojang.method(BLOCK, "defaultBlockState");
        var result = new ArrayList<String>();
        for (var block : (Iterable<?>) blockRegistry) {
            if ((boolean) Mojang.call(isSignalSource, Mojang.call(defaultState, block)))
                result.add(key(blockRegistry, block));
        }

        result.sort(null);
        return result;
    }

    /**
     * {@code block_transforms.json}, read by {@code Obsidian/Registries/BlockTransforms.cs}: per transform, each state's
     * {@code BlockState.rotate}/{@code mirror} result as a state id offset.
     */
    private void dumpBlockTransforms() throws IOException {
        var stateIds = mojang.get(BLOCK, "BLOCK_STATE_REGISTRY");
        var getId = mojang.method("net.minecraft.core.IdMapper", "getId", "java.lang.Object");
        var rotate = mojang.method(STATE_BASE, "rotate", "net.minecraft.world.level.block.Rotation");
        var mirror = mojang.method(STATE_BASE, "mirror", "net.minecraft.world.level.block.Mirror");
        var rotation = "net.minecraft.world.level.block.Rotation";
        var mirrors = "net.minecraft.world.level.block.Mirror";

        var json = new LinkedHashMap<String, Object>();
        json.put("_notes", "Vanilla " + version + " BlockState.rotate/mirror results per state id, stored as (result - state id).");
        json.put("rotateClockwise90", transformed(stateIds, getId, rotate, mojang.get(rotation, "CLOCKWISE_90")));
        json.put("rotate180", transformed(stateIds, getId, rotate, mojang.get(rotation, "CLOCKWISE_180")));
        json.put("rotateCounterclockwise90", transformed(stateIds, getId, rotate, mojang.get(rotation, "COUNTERCLOCKWISE_90")));
        json.put("mirrorLeftRight", transformed(stateIds, getId, mirror, mojang.get(mirrors, "LEFT_RIGHT")));
        json.put("mirrorFrontBack", transformed(stateIds, getId, mirror, mojang.get(mirrors, "FRONT_BACK")));
        Json.write(output.resolve("block_transforms.json"), json, false);
    }

    private int[] transformed(Object stateIds, Method getId, Method transform, Object argument) {
        var deltas = new int[states.size()];
        for (var id = 0; id < states.size(); id++)
            deltas[id] = (int) Mojang.call(getId, stateIds, Mojang.call(transform, states.get(id), argument)) - id;
        return deltas;
    }

    /**
     * {@code map_colors.bin}, read by {@code Obsidian/WorldData/Maps/MapColors.cs}: one byte per state, the id of
     * {@code BlockState.getMapColor}.
     */
    private void dumpMapColors() throws IOException {
        var getMapColor = mojang.method(STATE_BASE, "getMapColor", BLOCK_GETTER, BLOCK_POS);
        var colorId = mojang.field("net.minecraft.world.level.material.MapColor", "id");
        var colors = new byte[states.size()];
        for (var id = 0; id < states.size(); id++)
            colors[id] = (byte) Mojang.getInt(colorId, Mojang.call(getMapColor, states.get(id), emptyLevel, origin));
        Files.write(output.resolve("map_colors.bin"), colors);
    }

    /**
     * {@code wall_shape_covers.bin}, read by {@code Obsidian/WorldData/Features/Tree/WallShapeCovers.cs}: one byte per
     * state, which of {@code WallBlock}'s test shapes the bottom face of the state's collision shape covers
     * ({@code WallBlock.isCovered}, as {@code WallBlock.updateShape} checks the block above a wall).
     */
    private void dumpWallShapeCovers() throws IOException {
        var wall = "net.minecraft.world.level.block.WallBlock";
        var isCovered = mojang.method(wall, "isCovered", VOXEL_SHAPE, VOXEL_SHAPE);
        var getFaceShape = mojang.method(VOXEL_SHAPE, "getFaceShape", DIRECTION);
        var post = mojang.get(wall, "TEST_SHAPE_POST");
        var sides = (Map<?, ?>) mojang.get(wall, "TEST_SHAPES_WALL");
        // WallShapeCovers' bits after the post: north, east, south, west.
        var sideTests = new Object[] { sides.get(mojang.get(DIRECTION, "NORTH")), sides.get(mojang.get(DIRECTION, "EAST")),
            sides.get(mojang.get(DIRECTION, "SOUTH")), sides.get(mojang.get(DIRECTION, "WEST")) };
        var down = mojang.get(DIRECTION, "DOWN");

        var covers = new byte[states.size()];
        for (var id = 0; id < states.size(); id++) {
            var bottom = Mojang.call(getFaceShape, Mojang.call(getCollisionShape, states.get(id), emptyLevel, origin), down);
            var bits = bit(0, Mojang.call(isCovered, null, bottom, post));
            for (var side = 0; side < 4; side++)
                bits |= bit(1 + side, Mojang.call(isCovered, null, bottom, sideTests[side]));
            covers[id] = (byte) bits;
        }

        Files.write(output.resolve("wall_shape_covers.bin"), covers);
    }

    /**
     * {@code entities.json}, read by {@code Obsidian.SourceGenerators/Registry/EntityGenerator.cs}: every entity type
     * by id, with its {@code EntityType} properties, default attributes ({@code DefaultAttributes}), Mojang class name,
     * the synced data ids its class declares ({@code EntityDataAccessor} fields) and its superclass. The abstract
     * superclasses follow by class name, with their own synced data and superclass.
     */
    private void dumpEntities() throws IOException {
        var types = mojang.get(REGISTRIES, "ENTITY_TYPE");
        var attributes = mojang.get(REGISTRIES, "ATTRIBUTE");
        var getId = mojang.method("net.minecraft.core.IdMap", "getId", "java.lang.Object");
        var wrapAsHolder = mojang.method(REGISTRY, "wrapAsHolder", "java.lang.Object");
        var getDescriptionId = mojang.method(ENTITY_TYPE, "getDescriptionId");
        var canSerialize = mojang.method(ENTITY_TYPE, "canSerialize");
        var canSummon = mojang.method(ENTITY_TYPE, "canSummon");
        var fireImmune = mojang.method(ENTITY_TYPE, "fireImmune");
        var getDefaultLootTable = mojang.method(ENTITY_TYPE, "getDefaultLootTable");
        var getDimensions = mojang.method(ENTITY_TYPE, "getDimensions");
        var dimensions = "net.minecraft.world.entity.EntityDimensions";
        var width = mojang.method(dimensions, "width");
        var height = mojang.method(dimensions, "height");
        var fixed = mojang.method(dimensions, "fixed");
        var lootTableId = mojang.method("net.minecraft.resources.ResourceKey", "identifier");
        var defaultAttributes = "net.minecraft.world.entity.ai.attributes.DefaultAttributes";
        var hasSupplier = mojang.method(defaultAttributes, "hasSupplier", ENTITY_TYPE);
        var getSupplier = mojang.method(defaultAttributes, "getSupplier", ENTITY_TYPE);
        var supplier = "net.minecraft.world.entity.ai.attributes.AttributeSupplier";
        var hasAttribute = mojang.method(supplier, "hasAttribute", HOLDER);
        var getBaseValue = mojang.method(supplier, "getBaseValue", HOLDER);

        var classes = entityClasses();
        var entityClass = mojang.type(ENTITY);
        var json = new LinkedHashMap<String, Object>();
        var parents = new ArrayList<Class<?>>();
        for (var type : (Iterable<?>) types) {
            var entity = new LinkedHashMap<String, Object>();
            entity.put("id", Mojang.call(getId, types, type));
            entity.put("translation_key", Mojang.call(getDescriptionId, type));
            entity.put("serializable", Mojang.call(canSerialize, type));
            entity.put("summonable", Mojang.call(canSummon, type));
            entity.put("is_fire_immune", Mojang.call(fireImmune, type));
            var lootTable = (Optional<?>) Mojang.call(getDefaultLootTable, type);
            entity.put("loot_table", lootTable.map(key -> Mojang.call(lootTableId, key).toString()).orElse(null));
            var size = Mojang.call(getDimensions, type);
            entity.put("width", Mojang.call(width, size));
            entity.put("height", Mojang.call(height, size));
            entity.put("size_fixed", Mojang.call(fixed, size));

            if ((boolean) Mojang.call(hasSupplier, null, type)) {
                var values = new LinkedHashMap<String, Object>();
                var defaults = Mojang.call(getSupplier, null, type);
                for (var attribute : (Iterable<?>) attributes) {
                    var holder = Mojang.call(wrapAsHolder, attributes, attribute);
                    if ((boolean) Mojang.call(hasAttribute, defaults, holder))
                        values.put(key(attributes, attribute), Mojang.call(getBaseValue, defaults, holder));
                }
                entity.put("attributes", values);
            }

            var typeClass = classes.get(type);
            putClass(entity, typeClass);
            json.put(key(types, type), entity);

            for (var parent = typeClass.getSuperclass(); entityClass.isAssignableFrom(parent); parent = parent.getSuperclass()) {
                if (!parents.contains(parent))
                    parents.add(parent);
            }
        }

        // Superclasses that are also some type's class are already listed under that type.
        parents.removeAll(classes.values());
        for (var parent : parents) {
            var entry = new LinkedHashMap<String, Object>();
            putClass(entry, parent);
            entry.remove("class");
            if (json.put(mojang.simpleName(parent), entry) != null)
                throw new IllegalStateException("Two entity classes are named " + mojang.simpleName(parent));
        }

        Json.write(output.resolve("entities.json"), json, true);
    }

    /** Adds an entity class's name, the synced data its class declares by field name and id, and its superclass. */
    private void putClass(Map<String, Object> entry, Class<?> type) {
        var accessor = mojang.type("net.minecraft.network.syncher.EntityDataAccessor");
        var accessorId = mojang.method("net.minecraft.network.syncher.EntityDataAccessor", "id");
        var data = new ArrayList<Map.Entry<String, Integer>>();
        for (var field : type.getDeclaredFields()) {
            if (Modifier.isStatic(field.getModifiers()) && field.getType() == accessor) {
                field.setAccessible(true);
                data.add(Map.entry(mojang.fieldName(type, field), (int) Mojang.call(accessorId, Mojang.getStatic(field))));
            }
        }
        data.sort(Map.Entry.comparingByValue());

        entry.put("class", mojang.simpleName(type));
        if (!data.isEmpty()) {
            var meta = new LinkedHashMap<String, Integer>();
            data.forEach(item -> meta.put(item.getKey(), item.getValue()));
            entry.put("meta", meta);
        }
        if (type != mojang.type(ENTITY))
            entry.put("parent", mojang.simpleName(type.getSuperclass()));
    }

    /**
     * Each entity type's class, from the type argument of its {@code EntityType<T>} constant (the factories are lambdas
     * that don't name the class, and creating entities would need a level).
     */
    private Map<Object, Class<?>> entityClasses() {
        var entityType = mojang.type(ENTITY_TYPE);
        var classes = new HashMap<Object, Class<?>>();
        for (var field : entityType.getDeclaredFields()) {
            if (Modifier.isStatic(field.getModifiers()) && field.getType() == entityType
                && field.getGenericType() instanceof ParameterizedType generic && generic.getActualTypeArguments()[0] instanceof Class<?> type) {
                field.setAccessible(true);
                classes.put(Mojang.getStatic(field), type);
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
        json.put("configured_feature", featureGroups("CONFIGURED_FEATURE", "net.minecraft.data.worldgen.features"));
        json.put("placed_feature", featureGroups("PLACED_FEATURE", "net.minecraft.data.worldgen.placement"));
        Json.write(work.resolve("worldgen_groups.json"), json, true);
    }

    /** The keys of one registry ({@code Registries} field name) declared in a package's classes, by declaring class. */
    private Map<String, String> featureGroups(String registryName, String packageName) {
        var registry = mojang.get("net.minecraft.core.registries.Registries", registryName);
        var resourceKey = "net.minecraft.resources.ResourceKey";
        var keyType = mojang.type(resourceKey);
        var isFor = mojang.method(resourceKey, "isFor", resourceKey);
        var identifier = mojang.method(resourceKey, "identifier");
        var groups = new TreeMap<String, String>(); // String's natural order is ordinal
        for (var className : mojang.classesIn(packageName)) {
            var declaring = mojang.type(className);
            for (var field : declaring.getDeclaredFields()) {
                if (!Modifier.isStatic(field.getModifiers()) || field.getType() != keyType)
                    continue;
                var key = Mojang.getStatic(field);
                if (!(boolean) Mojang.call(isFor, key, registry))
                    continue;

                var id = Mojang.call(identifier, key).toString();
                var group = mojang.simpleName(declaring);
                var previous = groups.put(id, group);
                if (previous != null && !previous.equals(group))
                    throw new IllegalStateException(id + " is declared in both " + previous + " and " + group);
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
        var formats = new PacketFormats(mojang);
        var unread = new ArrayList<String>();
        var packetType = mojang.type(PACKET_TYPE);
        var flow = mojang.method(PACKET_TYPE, "flow");
        var id = mojang.method(PACKET_TYPE, "id");
        var flowId = mojang.method("net.minecraft.network.protocol.PacketFlow", "id");
        var classes = new HashMap<String, Class<?>>();
        var json = new TreeMap<String, Map<String, Object>>();
        for (var className : mojang.classes(name -> name.startsWith("net.minecraft.network.protocol.") && name.endsWith("PacketTypes"))) {
            for (var field : mojang.type(className).getDeclaredFields()) {
                if (!Modifier.isStatic(field.getModifiers()) || field.getType() != packetType
                    || !(field.getGenericType() instanceof ParameterizedType generic))
                    continue;

                // Generic packets (BundleDelimiterPacket<T>) are listed by their class.
                var argument = generic.getActualTypeArguments()[0];
                var type = (Class<?>) (argument instanceof ParameterizedType parameterized ? parameterized.getRawType() : argument);
                var packet = Mojang.getStatic(field);
                var direction = (String) Mojang.call(flowId, Mojang.call(flow, packet));
                var key = direction + " " + Mojang.call(id, packet);
                var previous = classes.put(key, type);
                if (previous != null && previous != type)
                    throw new IllegalStateException(key + " is both " + mojang.simpleName(previous) + " and " + mojang.simpleName(type));

                var writes = formats.writes(type);
                if (writes == null && !unread.contains(mojang.simpleName(type)))
                    unread.add(mojang.simpleName(type));
                json.computeIfAbsent(direction, name -> new TreeMap<>()).put(Mojang.call(id, packet).toString(), packetFields(type, writes));
            }
        }
        if (classes.isEmpty())
            throw new IllegalStateException("Found no packet types");
        if (!unread.isEmpty())
            System.out.println("Couldn't read how these packets are written, so their fields don't say whether they're sent: " + String.join(", ", unread));

        Json.write(work.resolve("packet_fields.json"), json, true);
        dumpPacketTypes(formats);
    }

    /**
     * {@code packet_types.json}, read by {@code Obsidian.SourceGenerators/Packets}: the vanilla records and enums the
     * packets write (their encodings refer to them as {@code {"kind": "type"}} and {@code {"kind": "enum"}}), by Mojang
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
     * A packet class's fields by Mojang name and type ({@link #typeName}), with how they're written when known
     * ({@code writes} isn't null): sent fields in the order they're written, then the rest.
     */
    private List<Object> packetFields(Class<?> type, Map<String, PacketFormats.Write> writes) {
        var fields = instanceFields(type);
        if (writes == null)
            return new ArrayList<>(fields);

        for (var field : fields) {
            var write = writes.get((String) field.get("name"));
            field.put("sent", write != null);
            if (write != null) {
                field.put("encoding", write.encoding());
                if (write.conditional())
                    field.put("conditional", true);
                if (write.repeated())
                    field.put("repeated", true);
                if (write.packed())
                    field.put("packed", true);
            }
        }
        var order = Comparator.comparingInt((Map<String, Object> field) -> {
            var write = writes.get((String) field.get("name"));
            return write == null ? Integer.MAX_VALUE : write.order();
        });
        return new ArrayList<>(fields.stream().sorted(order).toList());
    }

    /** A class's instance fields, its superclasses' first, by Mojang name and type. */
    private List<Map<String, Object>> instanceFields(Class<?> type) {
        var fields = type.getSuperclass() == null ? new ArrayList<Map<String, Object>>() : instanceFields(type.getSuperclass());
        for (var field : type.getDeclaredFields()) {
            if (Modifier.isStatic(field.getModifiers()) || field.isSynthetic())
                continue;

            var entry = new LinkedHashMap<String, Object>();
            entry.put("name", mojang.fieldName(type, field));
            entry.put("type", typeName(field.getGenericType()));
            fields.add(entry);
        }

        return fields;
    }

    /**
     * A Java type by Mojang's names, without packages and with nested classes after their outer class:
     * {@code int}, {@code Optional<Component>}, {@code ClientboundBossEventPacket.Operation}.
     */
    private String typeName(Type type) {
        return switch (type) {
            case Class<?> array when array.isArray() -> typeName(array.getComponentType()) + "[]";
            case Class<?> plain -> mojang.nestedName(plain);
            case ParameterizedType generic -> typeName(generic.getRawType())
                + Arrays.stream(generic.getActualTypeArguments()).map(this::typeName).collect(Collectors.joining(", ", "<", ">"));
            case GenericArrayType array -> typeName(array.getGenericComponentType()) + "[]";
            case WildcardType wildcard when wildcard.getLowerBounds().length > 0 -> "? super " + typeName(wildcard.getLowerBounds()[0]);
            case WildcardType wildcard when wildcard.getUpperBounds()[0] == Object.class -> "?";
            case WildcardType wildcard -> "? extends " + typeName(wildcard.getUpperBounds()[0]);
            default -> type.getTypeName(); // type variables
        };
    }

    // Helpers.

    /** The id of a registry entry, like {@code minecraft:stone}. */
    private String key(Object registry, Object value) {
        return Mojang.call(registryKey, registry, value).toString();
    }

    /** The boxes of a shape ({@code VoxelShape.toAabbs}), as min x, y, z then max x, y, z. */
    private List<double[]> boxes(Object shape) {
        var result = new ArrayList<double[]>();
        for (var box : (List<?>) Mojang.call(toAabbs, shape)) {
            var values = new double[6];
            for (var i = 0; i < 6; i++)
                values[i] = Mojang.getDouble(boxBounds[i], box);
            result.add(values);
        }

        return result;
    }

    // Directions by ordinal are down, up, north, south, west, east: pairs along y, z and x, negative first.

    /** The two axes (0 x, 1 y, 2 z) that span a direction's face, in x, y, z order. */
    private static int[] faceAxes(Object direction) {
        return switch (((Enum<?>) direction).ordinal() / 2) {
            case 0 -> new int[] { 0, 2 }; // down, up
            case 1 -> new int[] { 0, 1 }; // north, south
            default -> new int[] { 1, 2 }; // west, east
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
            if (values[i] == value)
                return i;
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

    /**
     * Mojang's names for the obfuscated server, from its ProGuard mappings ({@code server.txt}): a class line
     * {@code net.minecraft.Foo -> abc:} followed by indented member lines {@code type name(parameters) -> obf}, where
     * method lines may carry line numbers ({@code 12:34:}). Members are found by their Mojang name (and, for methods,
     * their parameter types as the mappings spell them) on the class the mappings list them under, which is the class
     * that declares or overrides them.
     */
    static final class Mojang {
        private static final Pattern CLASS = Pattern.compile("^(\\S+) -> (\\S+):$");
        private static final Pattern MEMBER = Pattern.compile("^\\s+(?:\\d+:\\d+:)?\\S+ ([^\\s(]+)(\\(([^)]*)\\))?(?::\\d+:\\d+)? -> (\\S+)$");
        private static final Map<String, Class<?>> PRIMITIVES = Map.of("int", int.class, "boolean", boolean.class,
            "long", long.class, "double", double.class, "float", float.class, "byte", byte.class, "short", short.class,
            "char", char.class);

        private final Map<String, String> obfuscatedClasses = new HashMap<>();
        private final Map<String, String> mojangClasses = new HashMap<>();
        // Per Mojang class name: "name" for fields, "name(parameter types)" for methods, to obfuscated name.
        private final Map<String, Map<String, String>> members = new HashMap<>();
        // Per Mojang class name: obfuscated field name to Mojang name.
        private final Map<String, Map<String, String>> fieldNames = new HashMap<>();
        // Per Mojang class name: "obfuscated(Mojang parameter types)" to the method's Mojang name.
        private final Map<String, Map<String, String>> methodNames = new HashMap<>();

        static Mojang read(Path path) throws IOException {
            var mojang = new Mojang();
            Map<String, String> classMembers = null;
            Map<String, String> classFields = null;
            Map<String, String> classMethods = null;
            for (var line : Files.readAllLines(path, StandardCharsets.UTF_8)) {
                if (line.isEmpty() || line.stripLeading().startsWith("#"))
                    continue;

                Matcher match;
                if (!Character.isWhitespace(line.charAt(0)) && (match = CLASS.matcher(line)).matches()) {
                    mojang.obfuscatedClasses.put(match.group(1), match.group(2));
                    mojang.mojangClasses.put(match.group(2), match.group(1));
                    classMembers = mojang.members.computeIfAbsent(match.group(1), name -> new HashMap<>());
                    classFields = mojang.fieldNames.computeIfAbsent(match.group(1), name -> new HashMap<>());
                    classMethods = mojang.methodNames.computeIfAbsent(match.group(1), name -> new HashMap<>());
                } else if (classMembers != null && (match = MEMBER.matcher(line)).matches()) {
                    var isMethod = match.group(2) != null;
                    classMembers.putIfAbsent(isMethod ? match.group(1) + match.group(2) : match.group(1), match.group(4));
                    if (isMethod)
                        classMethods.putIfAbsent(match.group(4) + match.group(2), match.group(1));
                    else
                        classFields.put(match.group(4), match.group(1));
                }
            }

            return mojang;
        }

        /** A class by Mojang name, or a Java type name ({@code int}, {@code java.lang.Object}, arrays with {@code []}). */
        Class<?> type(String name) {
            if (name.endsWith("[]"))
                return type(name.substring(0, name.length() - 2)).arrayType();
            if (PRIMITIVES.containsKey(name))
                return PRIMITIVES.get(name);
            try {
                return Class.forName(obfuscatedClasses.getOrDefault(name, name), false, VanillaDumper.class.getClassLoader());
            } catch (ClassNotFoundException e) {
                throw new IllegalStateException("Missing class " + name, e);
            }
        }

        /** The Mojang names of the classes directly in a package (not its subpackages), sorted. */
        List<String> classesIn(String packageName) {
            return classes(name -> name.lastIndexOf('.') == packageName.length() && name.startsWith(packageName + ".")
                && !name.endsWith(".package-info"));
        }

        /** The Mojang names of the classes matching a filter, sorted. */
        List<String> classes(Predicate<String> filter) {
            return obfuscatedClasses.keySet().stream().filter(filter).sorted().toList();
        }

        /** The Mojang simple name of a class, like {@code StairBlock} (the part after the last '.' or '$'). */
        String simpleName(Class<?> type) {
            var name = mojangClasses.getOrDefault(type.getName(), type.getName());
            return name.substring(Math.max(name.lastIndexOf('.'), name.lastIndexOf('$')) + 1);
        }

        /** The Mojang name of a class without its package, like {@code BossEvent.BossBarColor} for a nested class. */
        String nestedName(Class<?> type) {
            var name = mojangClasses.getOrDefault(type.getName(), type.getName());
            return name.substring(name.lastIndexOf('.') + 1).replace('$', '.');
        }

        /** The Mojang name of a class by its runtime name, or the name itself for classes the mappings don't rename. */
        String className(String name) {
            return mojangClasses.getOrDefault(name, name);
        }

        /**
         * The Mojang name of a method a class declares, by its obfuscated name and parameter types as the mappings
         * spell them ({@code int,net.minecraft.core.BlockPos}); null when the mappings don't list it.
         */
        String methodName(Class<?> owner, String obfuscated, String parameterTypes) {
            var mojangOwner = mojangClasses.get(owner.getName());
            return mojangOwner == null ? null : methodNames.getOrDefault(mojangOwner, Map.of()).get(obfuscated + "(" + parameterTypes + ")");
        }

        /** The Mojang name of a field declared by a class. */
        String fieldName(Class<?> owner, Field field) {
            var mojangOwner = mojangClasses.getOrDefault(owner.getName(), owner.getName());
            return fieldNames.getOrDefault(mojangOwner, Map.of()).getOrDefault(field.getName(), field.getName());
        }

        Field field(String owner, String name) {
            try {
                var field = type(owner).getDeclaredField(obfuscatedMember(owner, name));
                field.setAccessible(true);
                return field;
            } catch (NoSuchFieldException e) {
                throw new IllegalStateException("Missing field " + owner + "." + name, e);
            }
        }

        Method method(String owner, String name, String... parameterTypes) {
            var parameters = new Class<?>[parameterTypes.length];
            for (var i = 0; i < parameters.length; i++)
                parameters[i] = type(parameterTypes[i]);
            try {
                var method = type(owner).getDeclaredMethod(obfuscatedMember(owner, name + "(" + String.join(",", parameterTypes) + ")"), parameters);
                method.setAccessible(true);
                return method;
            } catch (NoSuchMethodException e) {
                throw new IllegalStateException("Missing method " + owner + "." + name, e);
            }
        }

        Constructor<?> constructor(String owner, String... parameterTypes) {
            var parameters = new Class<?>[parameterTypes.length];
            for (var i = 0; i < parameters.length; i++)
                parameters[i] = type(parameterTypes[i]);
            try {
                var constructor = type(owner).getDeclaredConstructor(parameters);
                constructor.setAccessible(true);
                return constructor;
            } catch (NoSuchMethodException e) {
                throw new IllegalStateException("Missing constructor of " + owner, e);
            }
        }

        /** A static field's value. */
        Object get(String owner, String name) {
            return getStatic(field(owner, name));
        }


        private String obfuscatedMember(String owner, String member) {
            var name = members.getOrDefault(owner, Map.of()).get(member);
            if (name == null)
                throw new IllegalStateException("No mapping for " + owner + "." + member);
            return name;
        }

        static Object call(Method method, Object target, Object... arguments) {
            try {
                return method.invoke(target, arguments);
            } catch (InvocationTargetException e) {
                throw new IllegalStateException("Vanilla's " + method + " failed", e.getCause());
            } catch (IllegalAccessException e) {
                throw new IllegalStateException(e);
            }
        }

        static Object create(Constructor<?> constructor, Object... arguments) {
            try {
                return constructor.newInstance(arguments);
            } catch (InvocationTargetException e) {
                throw new IllegalStateException("Vanilla's " + constructor + " failed", e.getCause());
            } catch (ReflectiveOperationException e) {
                throw new IllegalStateException(e);
            }
        }

        static Object getStatic(Field field) {
            field.setAccessible(true);
            return get(field, null);
        }

        static Object get(Field field, Object target) {
            try {
                return field.get(target);
            } catch (IllegalAccessException e) {
                throw new IllegalStateException(e);
            }
        }

        static int getInt(Field field, Object target) {
            try {
                return field.getInt(target);
            } catch (IllegalAccessException e) {
                throw new IllegalStateException(e);
            }
        }

        static double getDouble(Field field, Object target) {
            try {
                return field.getDouble(target);
            } catch (IllegalAccessException e) {
                throw new IllegalStateException(e);
            }
        }
    }

    /** A minimal JSON writer for maps, lists, int arrays, strings, numbers, booleans and null. */
    static final class Json {
        static void write(Path path, Object value, boolean indented) throws IOException {
            var builder = new StringBuilder();
            append(builder, value, indented ? "\n" : null);
            if (indented)
                builder.append('\n');
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
                    if (inner != null)
                        builder.append(inner);
                    string(builder, entry.getKey().toString());
                    builder.append(inner != null ? ": " : ":");
                    append(builder, entry.getValue(), inner);
                    first = false;
                }
                if (newline != null && !map.isEmpty())
                    builder.append(newline);
                builder.append('}');
            } else if (value instanceof int[] array) {
                builder.append('[');
                for (var i = 0; i < array.length; i++)
                    builder.append(i == 0 ? "" : ",").append(array[i]);
                builder.append(']');
            } else if (value instanceof double[] array) {
                builder.append('[');
                for (var i = 0; i < array.length; i++)
                    builder.append(i == 0 ? "" : ",").append(array[i]);
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
                if (c == '"' || c == '\\')
                    builder.append('\\').append(c);
                else if (c < 0x20)
                    builder.append(String.format("\\u%04x", (int) c));
                else
                    builder.append(c);
            }
            builder.append('"');
        }
    }
}
