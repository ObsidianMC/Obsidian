using Obsidian.API.Blocks;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.Registries;

/// <summary>
/// Vanilla's per-state block physics (collision, replaceability, fluids, sturdy faces), used by world generation.
/// </summary>
/// <remarks>
/// Loaded from <c>Assets/block_physics.json</c>, which was dumped from vanilla 1.21.11 for every block state, along with
/// each block's vanilla class, block entity type and whether it's a signal source, and the states that random-tick.
/// Each state packs its flags into one int:
/// bit 0 air, 1 blocks motion, 2 solid, 3 replaceable, 4 liquid, 5 fluid source, 6 full collision cube,
/// 7 sturdy center (up), 8 sturdy rigid (up), 9 redstone conductor, 10 block entity, 11-16 sturdy full faces
/// (one bit per <see cref="BlockFace"/>), 17-20 light emission, 21-24 fluid amount, 25-27 fluid kind, 28 empty collision,
/// 29 full top collision face, 30 solid render, 31 sturdy center (down).
/// Fluid physics pack into a second int per state: bit 0 collision shape is vanilla's full block shape, 1 collision shape
/// is the empty shape, 2 <c>LiquidBlockContainer</c>, 3 <c>FlowingFluid.canHoldAnyFluid</c>, 4-7
/// <c>FlowingFluid.canHoldSpecificFluid</c> for water, flowing water, lava and flowing lava, 16-31 the collision face set
/// (an index into <c>collisionFaceSets</c>, whose six entries per <see cref="BlockFace"/> index <c>collisionFaces</c>).
/// </remarks>
internal static class BlockPhysics
{
    private static readonly Lazy<PhysicsData> data = new(Load);

    // The per-state tables, loaded with the data on first use, without a Lazy check on every lookup.
    private static class Tables
    {
        public static readonly int[] Flags = data.Value.Flags;
    }

    /// <summary>
    /// Vanilla <c>BlockState.blocksMotion()</c>: the block has collision (leaves and logs do, plants and snow layers don't).
    /// </summary>
    public static bool BlocksMotion(this IBlock block) => Has(block, 1);

    /// <summary>
    /// Vanilla <c>BlockState.isSolid()</c>.
    /// </summary>
    public static bool IsSolid(this IBlock block) => Has(block, 2);

    /// <summary>
    /// Vanilla <c>BlockState.canBeReplaced()</c>: air, fluids, short grass, snow layers and the like.
    /// </summary>
    public static bool CanBeReplaced(this IBlock block) => Has(block, 3);

    /// <summary>
    /// Vanilla <c>BlockState.liquid()</c>: water and lava blocks.
    /// </summary>
    public static bool IsLiquidBlock(this IBlock block) => Has(block, 4);

    public static bool IsCollisionShapeFullBlock(this IBlock block) => Has(block, 6);

    /// <summary>
    /// Vanilla <c>getCollisionShape(...).isEmpty()</c>: nothing to collide with (air, plants, one snow layer). Unlike
    /// <see cref="BlocksMotion"/>, low blocks such as carpets and thicker snow layers do have a collision shape.
    /// </summary>
    public static bool HasEmptyCollision(this IBlock block) => Has(block, 28);

    /// <summary>
    /// Vanilla <c>Block.isFaceFull(getCollisionShape(...), UP)</c>: the top of the collision shape covers the whole block,
    /// so players can stand on it (used to find the world spawn).
    /// </summary>
    public static bool HasFullTopCollisionFace(this IBlock block) => Has(block, 29);

    public static bool HasBlockEntity(this IBlock block) => Has(block, 10);

    /// <summary>
    /// Vanilla <c>BlockState.isRedstoneConductor()</c>: redstone power passes through the block.
    /// </summary>
    public static bool IsRedstoneConductor(this IBlock block) => Has(block, 9);

    /// <summary>
    /// Vanilla <c>BlockState.isSignalSource()</c>: levers, buttons, redstone torches, repeaters and the like. It's the same for
    /// every state of a block.
    /// </summary>
    public static bool IsSignalSource(this IBlock block) => data.Value.SignalSources.Contains(block.UnlocalizedName);

    /// <summary>
    /// The type of the block entity the block has (e.g. <c>minecraft:mob_spawner</c> for spawners): vanilla's, or the
    /// one a plugin registered for its block. <c>null</c> when it has none.
    /// </summary>
    public static string? BlockEntityType(this IBlock block) => block is API.Blocks.PluginBlock plugin
        ? BlockEntityTypesRegistry.Of(plugin.Type)?.Identifier
        : data.Value.BlockEntityTypes.GetValueOrDefault(block.UnlocalizedName);

    /// <summary>
    /// Vanilla <c>isFaceSturdy(..., face, SupportType.FULL)</c>.
    /// </summary>
    public static bool IsFaceSturdy(this IBlock block, BlockFace face) => Has(block, 11 + (int)face);

    /// <summary>
    /// Vanilla <c>isFaceSturdy(..., UP, SupportType.CENTER)</c>.
    /// </summary>
    public static bool IsTopCenterSturdy(this IBlock block) => Has(block, 7);

    /// <summary>
    /// Vanilla <c>isFaceSturdy(..., UP, SupportType.RIGID)</c>: what repeaters, comparators and pressure plates stand
    /// on.
    /// </summary>
    public static bool IsTopRigidSturdy(this IBlock block) => Has(block, 8);

    /// <summary>
    /// Vanilla <c>Block.canSupportCenter(..., DOWN)</c>: something can hang from the center of the bottom face.
    /// </summary>
    public static bool IsBottomCenterSturdy(this IBlock block) => Has(block, 31);

    /// <summary>
    /// Vanilla <c>BlockState.isSolidRender()</c>: an opaque full cube.
    /// </summary>
    public static bool IsSolidRender(this IBlock block) => Has(block, 30);

    /// <summary>
    /// Whether the block is a vanilla <c>FallingBlock</c> (sand, gravel, concrete powder, anvils, dragon eggs).
    /// </summary>
    public static bool IsFallingBlock(this IBlock block) =>
        block.BlockClass() is "ColoredFallingBlock" or "SandBlock" or "ConcretePowderBlock" or "AnvilBlock" or "DragonEggBlock";

    /// <summary>
    /// Whether the block falls when there's nothing below it: a vanilla <c>FallingBlock</c> or a <c>BrushableBlock</c>
    /// (suspicious sand and gravel).
    /// </summary>
    public static bool IsGravityAffected(this IBlock block) => block.IsFallingBlock() || block.BlockClass() == "BrushableBlock";

    /// <summary>
    /// Vanilla <c>FallingBlock.isFree</c>: a falling block can fall into the block.
    /// </summary>
    /// <remarks>
    /// Vanilla also checks for air, fire and liquids, but those blocks are all replaceable.
    /// </remarks>
    public static bool IsFreeForFallingBlock(this IBlock block) => block.CanBeReplaced();

    public static int LightEmission(this IBlock block) => (Flags(block) >> 17) & 15;

    public static FluidKind GetFluid(this IBlock block) => (FluidKind)((Flags(block) >> 25) & 7);

    public static bool HasFluid(this IBlock block) => block.GetFluid() != FluidKind.Empty;

    public static bool IsFluidSource(this IBlock block) => Has(block, 5);

    public static int FluidAmount(this IBlock block) => (Flags(block) >> 21) & 15;

    /// <summary>
    /// The vanilla block class (e.g. <c>LeavesBlock</c>, <c>FlowerBlock</c>), which decides placement rules.
    /// </summary>
    public static string BlockClass(this IBlock block) =>
        data.Value.BlockClasses.GetValueOrDefault(block.UnlocalizedName, "Block");

    /// <summary>
    /// The block's state id: the same id vanilla uses, unique per block and property combination.
    /// </summary>
    public static int StateId(this IBlock block) => block.GetHashCode();

    /// <summary>
    /// Whether two blocks are the same block state.
    /// </summary>
    public static bool IsSameState(this IBlock block, IBlock other) => block.GetHashCode() == other.GetHashCode();

    /// <summary>
    /// Vanilla <c>getCollisionShape(...) == Shapes.block()</c>: the collision shape is the full block instance.
    /// </summary>
    public static bool HasBlockCollisionShape(this IBlock block) => HasFluidBit(block, 0);

    /// <summary>
    /// Vanilla <c>getCollisionShape(...) == Shapes.empty()</c>: the collision shape is the empty instance.
    /// </summary>
    public static bool HasEmptyCollisionShape(this IBlock block) => HasFluidBit(block, 1);

    /// <summary>
    /// Whether the block is a vanilla <c>LiquidBlockContainer</c> (waterloggable blocks, kelp and seagrass), which takes
    /// fluids through <c>placeLiquid</c> instead of being replaced.
    /// </summary>
    public static bool IsLiquidBlockContainer(this IBlock block) => HasFluidBit(block, 2);

    /// <summary>
    /// Vanilla <c>FlowingFluid.canHoldAnyFluid</c>: fluids may flow into the block (destroying it unless it's a container).
    /// </summary>
    public static bool CanHoldAnyFluid(this IBlock block) => HasFluidBit(block, 3);

    /// <summary>
    /// Vanilla <c>FlowingFluid.canHoldSpecificFluid</c>: containers only take the fluids their <c>canPlaceLiquid</c>
    /// accepts (a water source for waterloggable blocks); every other block takes any fluid.
    /// </summary>
    public static bool CanHoldSpecificFluid(this IBlock block, FluidKind fluid) =>
        fluid == FluidKind.Empty ? !block.IsLiquidBlockContainer() : HasFluidBit(block, 3 + (int)fluid);

    /// <summary>
    /// Vanilla <c>Shapes.mergedFaceOccludes</c> for collision shapes: together, the face of <paramref name="from"/>
    /// toward <paramref name="direction"/> and the opposite face of <paramref name="to"/> cover the whole face between
    /// them, so fluids can't pass.
    /// </summary>
    public static bool MergedFaceOccludes(IBlock from, IBlock to, BlockFace direction)
    {
        if (from.HasBlockCollisionShape() || to.HasBlockCollisionShape())
            return true;

        if (PluginShapes(from.StateId()) is not null || PluginShapes(to.StateId()) is not null)
            return CoversFace(CollisionFace(from, direction), CollisionFace(to, direction.Opposite()));

        return data.Value.FaceOcclusion.GetOrAdd((from.StateId(), to.StateId(), direction),
            key => CoversFace(CollisionFace(from, key.Direction), CollisionFace(to, key.Direction.Opposite())));
    }

    // The boxes (min and max on each of the face's two axes) where the collision shape touches the given face.
    private static double[][] CollisionFace(IBlock block, BlockFace face)
    {
        if (PluginShapes(block.StateId()) is PluginStateShapes own)
            return own.CollisionFaces[(int)face];

        var physics = data.Value;
        var set = (int)((uint)FluidFlags(block) >> 16);
        return physics.CollisionFaces[physics.CollisionFaceSets[set][(int)face]];
    }

    // Whether the union of two faces' boxes covers the unit square, checked on the grid their edges make.
    private static bool CoversFace(double[][] first, double[][] second)
    {
        if (first.Length == 0 && second.Length == 0)
            return false;

        double[][] boxes = [.. first, .. second];
        var us = Edges(boxes, 0);
        var vs = Edges(boxes, 1);
        for (var i = 0; i < us.Count - 1; i++)
        {
            var u = (us[i] + us[i + 1]) / 2;
            for (var j = 0; j < vs.Count - 1; j++)
            {
                var v = (vs[j] + vs[j + 1]) / 2;
                if (!boxes.Any(box => box[0] <= u && u <= box[2] && box[1] <= v && v <= box[3]))
                    return false;
            }
        }

        return true;

        static List<double> Edges(double[][] boxes, int axis)
        {
            var edges = new List<double> { 0, 1 };
            foreach (var box in boxes)
            {
                edges.Add(Math.Clamp(box[axis], 0, 1));
                edges.Add(Math.Clamp(box[axis + 2], 0, 1));
            }

            edges.Sort();
            var distinct = new List<double>();
            foreach (var edge in edges)
            {
                if (distinct.Count == 0 || edge - distinct[^1] > 1.0E-7)
                    distinct.Add(edge);
            }

            return distinct;
        }
    }

    private static bool Has(IBlock block, int bit) => (Flags(block) & (1 << bit)) != 0;

    /// <summary>
    /// The physics flags of a state, by state id: what the <see cref="IBlock"/> extensions here read, for code that works
    /// with state ids.
    /// </summary>
    public static int Flags(int stateId)
    {
        var flags = Tables.Flags;

        if ((uint)stateId < (uint)flags.Length)
            return flags[stateId];

        return BlocksRegistry.TryGetPluginBlock(stateId, out var type) ? PluginFlags(type, stateId) : 0;
    }

    // The flags a plugin block keeps from its template state: its shape's, not those that come from what the vanilla
    // block is (air, being replaceable, fluids, a block entity, light emission).
    private const int TemplateFlags = 1 << 1 | 1 << 2 | 1 << 6 | 1 << 7 | 1 << 8 | 1 << 9 | 0b111111 << 11
        | 1 << 28 | 1 << 29 | 1 << 30 | 1 << 31;

    // A plugin block state's flags: its template state's (see BlocksRegistry.TemplateStateOf), with the state's own
    // light emission, and a block entity when the plugin registered a block entity type for the block.
    private static int PluginFlags(BlockType type, int stateId)
    {
        var settings = type.Settings;
        var flags = Tables.Flags[BlocksRegistry.TemplateStateOf(type)] & TemplateFlags;

        // Cobweb, the template without collision, is still solid in vanilla's legacy sense; the block isn't.
        if (!settings.HasCollision)
            flags &= ~(1 << 1 | 1 << 2);

        if (BlockEntityTypesRegistry.Of(type) is not null)
            flags |= 1 << 10;

        var index = stateId - type.FirstStateId;
        if (settings.ShapesOf(index) is PluginStateShapes own)
            flags = OwnShapeFlags(flags, own);

        return flags | Math.Clamp(settings.LightEmissionOf(index), 0, 15) << 17;
    }

    // The flags that come from a collision shape: a full collision cube (6), sturdy faces and centers (7, 8, 11-16,
    // 31) and a full top face (29).
    private const int FullShapeFlags = 1 << 6 | 1 << 7 | 1 << 8 | 0b111111 << 11 | 1 << 29 | 1 << 31;

    // Registration computes volume and face coverage once, independently of how the union was split into boxes.
    private static int OwnShapeFlags(int flags, PluginStateShapes own)
    {
        flags = (flags & ~(FullShapeFlags | 1 << 28)) | own.PhysicsFlags;
        if ((own.PhysicsFlags & (1 << 6)) == 0)
            flags &= ~(1 << 9 | 1 << 30);

        return flags;
    }

    // The vanilla state whose shapes describe a state: itself, a plugin block's template, or air for an unknown id.
    private static int ShapeState(int stateId)
    {
        if ((uint)stateId < (uint)Tables.Flags.Length)
            return stateId;

        return BlocksRegistry.TryGetPluginBlock(stateId, out var type) ? BlocksRegistry.TemplateStateOf(type) : 0;
    }

    /// <summary>Whether a state has a physics flag (see <see cref="Flags(int)"/>).</summary>
    public static bool Has(int stateId, int bit) => (Flags(stateId) & (1 << bit)) != 0;

    /// <summary><see cref="IBlock.IsAir"/> by state id: air, cave air and void air.</summary>
    public static bool IsAir(int stateId) => Has(stateId, 0);

    /// <summary><see cref="IsFaceSturdy(IBlock, BlockFace)"/> by state id.</summary>
    public static bool IsFaceSturdy(int stateId, BlockFace face) => Has(stateId, 11 + (int)face);

    private static bool HasFluidBit(IBlock block, int bit) => (FluidFlags(block) & (1 << bit)) != 0;

    private static int FluidFlags(IBlock block)
    {
        var flags = data.Value.FluidFlags;
        var id = block.GetHashCode();

        if ((uint)id < (uint)flags.Length)
            return flags[id];

        if (PluginShapes(id) is PluginStateShapes own)
            return (own.PhysicsFlags & (1 << 6)) != 0 ? 1 : own.Shapes.Collision.Count == 0 ? 2 : 0;

        // A plugin block without its own shapes uses its template, but holds no fluid.
        const int shapeFlags = 0b11 | unchecked((int)0xFFFF0000);
        return flags[ShapeState(id)] & shapeFlags;
    }

    private static int Flags(IBlock block) => Flags(block.GetHashCode());

    /// <summary>
    /// Vanilla state shapes captured with empty collision context, in block-local coordinates, or a plugin block
    /// state's own collision shape (see <see cref="PluginShapes"/>), which is its visual shape too, as vanilla's
    /// visual shape is the collision shape unless a block says otherwise.
    /// </summary>
    public static IReadOnlyList<double[]> ShapeBoxes(int state, bool visual = false) =>
        PluginShapes(state)?.CollisionBoxes
        ?? data.Value.ShapeBoxes[(visual ? data.Value.VisualShapes : data.Value.CollisionShapes)[ShapeState(state)]];

    /// <summary>
    /// Vanilla's selection/picking shape, distinct from the camera's visual shape, or a plugin block state's own
    /// outline (see <see cref="PluginShapes"/>).
    /// </summary>
    public static IReadOnlyList<double[]> OutlineBoxes(int state) =>
        PluginShapes(state)?.OutlineBoxes ?? data.Value.ShapeBoxes[data.Value.OutlineShapes[ShapeState(state)]];

    /// <summary>
    /// The shapes a plugin block's <see cref="BlockProperties.StateShapes"/> gave a state when it registered, or null
    /// for vanilla's states and plugin states that keep their template's shapes. Both sides read them from the
    /// block's settings, which each side's <c>ConfigureRegistry</c> made; a block entity's shapes win over them where
    /// the caller reads those first.
    /// </summary>
    internal static PluginStateShapes? PluginShapes(int stateId)
    {
        if ((uint)stateId < (uint)Tables.Flags.Length || !BlocksRegistry.TryGetPluginBlock(stateId, out var type))
            return null;

        return type.Settings.ShapesOf(stateId - type.FirstStateId);
    }

    public static bool Suffocates(int state) => data.Value.Suffocating[ShapeState(state)];

    /// <summary>Moves shapes captured at the origin to a state's position-dependent offset (flowers, bamboo, etc.).</summary>
    public static (double X, double Y, double Z) ShapeOffset(int state, int x, int z)
    {
        var offset = data.Value.ShapeOffsets[ShapeState(state)];
        if (offset.Length == 0)
            return default;

        // Vanilla's Mth position seed and float fractions must match the offset used by the block's shape.
        var seed = unchecked((long)(x * 3129871) ^ z * 116129781L);
        seed = unchecked(seed * seed * 42317861L + seed * 11L) >> 16;
        var dx = Math.Clamp(((float)(seed & 15) / 15f - .5) * .5, -offset[3], offset[3]);
        var dz = Math.Clamp(((float)((seed >> 8) & 15) / 15f - .5) * .5, -offset[3], offset[3]);
        var dy = offset[1] == 0 ? 0 : ((float)((seed >> 4) & 15) / 15f - 1.0) * offset[4];
        return (dx - offset[0], dy - offset[1], dz - offset[2]);
    }
    public static float MiningHardness(int state)
    {
        if ((uint)state < (uint)Tables.Flags.Length)
            return data.Value.Hardness[state];

        return BlocksRegistry.TryGetPluginBlock(state, out var type) ? type.Settings.Hardness : 0;
    }

    /// <summary>
    /// Whether a state counts toward a chunk section's random ticks, like vanilla's per-section count: its block
    /// (vanilla's <c>BlockState.isRandomlyTicking</c>) or its fluid (lava) ticks on random ticks. Random ticks only
    /// sample sections that hold such a state.
    /// </summary>
    /// <remarks>
    /// Plugin blocks always count: whether one grows on random ticks (a plugin's sapling) changes when its plugin
    /// reloads, which sections don't hear about. Vanilla worlds hold none, so their random ticks stay vanilla's.
    /// </remarks>
    public static bool IsRandomlyTicking(int stateId)
    {
        var ticking = data.Value.RandomTicking;
        if ((uint)stateId < (uint)ticking.Length)
            return ticking[stateId];

        return BlocksRegistry.TryGetPluginBlock(stateId, out _);
    }

    public static bool RequiresCorrectTool(int state)
    {
        if ((uint)state < (uint)Tables.Flags.Length)
            return data.Value.RequiresTool[state];

        return BlocksRegistry.TryGetPluginBlock(state, out var type) && type.Settings.RequiresCorrectTool;
    }

    private static PhysicsData Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Assets.block_physics.json")
            ?? throw new InvalidOperationException("Missing block physics asset.");
        using var document = JsonDocument.Parse(stream);

        var root = document.RootElement;
        return new PhysicsData(
            root.GetProperty("flags").EnumerateArray().Select(value => value.GetInt32()).ToArray(),
            root.GetProperty("blockClasses").EnumerateObject().ToDictionary(entry => entry.Name, entry => entry.Value.GetString()!),
            root.GetProperty("blockEntityTypes").EnumerateObject().ToDictionary(entry => entry.Name, entry => entry.Value.GetString()!),
            root.GetProperty("blockEntityTypeIds").EnumerateObject().ToDictionary(entry => entry.Name, entry => entry.Value.GetInt32()),
            root.GetProperty("signalSources").EnumerateArray().Select(entry => entry.GetString()!).ToHashSet(),
            root.GetProperty("fluidFlags").EnumerateArray().Select(value => value.GetInt32()).ToArray(),
            root.GetProperty("collisionFaceSets").EnumerateArray()
                .Select(set => set.EnumerateArray().Select(face => face.GetInt32()).ToArray()).ToArray(),
            root.GetProperty("collisionFaces").EnumerateArray()
                .Select(face => face.EnumerateArray().Select(box => box.EnumerateArray().Select(value => value.GetDouble()).ToArray()).ToArray())
                .ToArray(),
            root.GetProperty("shapeBoxes").EnumerateArray().Select(shape => shape.EnumerateArray()
                .Select(box => box.EnumerateArray().Select(value => value.GetDouble()).ToArray()).ToArray()).ToArray(),
            root.GetProperty("collisionShapes").EnumerateArray().Select(value => value.GetInt32()).ToArray(),
            root.GetProperty("visualShapes").EnumerateArray().Select(value => value.GetInt32()).ToArray(),
            root.GetProperty("outlineShapes").EnumerateArray().Select(value => value.GetInt32()).ToArray(),
            root.GetProperty("suffocating").EnumerateArray().Select(value => value.GetBoolean()).ToArray(),
            root.GetProperty("shapeOffsets").EnumerateArray()
                .Select(value => value.EnumerateArray().Select(number => number.GetDouble()).ToArray())
                .ToArray(),
            root.GetProperty("hardness").EnumerateArray().Select(value => value.GetSingle()).ToArray(),
            root.GetProperty("requiresTool").EnumerateArray().Select(value => value.GetBoolean()).ToArray(),
            RandomTickingStates(root));
    }

    private static bool[] RandomTickingStates(JsonElement root)
    {
        var ticking = new bool[root.GetProperty("flags").GetArrayLength()];
        foreach (var state in root.GetProperty("randomTicking").EnumerateArray())
            ticking[state.GetInt32()] = true;

        return ticking;
    }

    private sealed record PhysicsData(int[] Flags, Dictionary<string, string> BlockClasses, Dictionary<string, string> BlockEntityTypes,
        Dictionary<string, int> BlockEntityTypeIds, HashSet<string> SignalSources, int[] FluidFlags, int[][] CollisionFaceSets,
        double[][][] CollisionFaces, double[][][] ShapeBoxes, int[] CollisionShapes, int[] VisualShapes, int[] OutlineShapes, bool[] Suffocating,
        double[][] ShapeOffsets, float[] Hardness, bool[] RequiresTool, bool[] RandomTicking)
    {
        // Like vanilla's occlusion cache in FlowingFluid: the same pairs of partial blocks come up again and again.
        public ConcurrentDictionary<(int From, int To, BlockFace Direction), bool> FaceOcclusion { get; } = new();
    }
}

/// <summary>
/// Fluid carried by a block state. The values are how <c>block_physics.json</c> encodes fluids, which
/// <see cref="BlockPhysics.CanHoldSpecificFluid"/> relies on; they aren't vanilla's fluid registry ids, which list flowing
/// water before water.
/// </summary>
internal enum FluidKind
{
    Empty,
    Water,
    FlowingWater,
    Lava,
    FlowingLava
}
