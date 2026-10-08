using System.Reflection;
using System.Text.Json;

namespace Obsidian.Registries;

/// <summary>
/// Vanilla's per-state block physics (collision, replaceability, fluids, sturdy faces), used by world generation.
/// </summary>
/// <remarks>
/// Loaded from <c>Assets/block_physics.json</c>, which was dumped from vanilla 1.21.11 for every block state, along with
/// each block's vanilla class, block entity type and whether it's a signal source.
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
    /// The type of the block entity vanilla creates for the block (e.g. <c>minecraft:mob_spawner</c> for spawners), or
    /// <c>null</c> when it has none.
    /// </summary>
    public static string? BlockEntityType(this IBlock block) => data.Value.BlockEntityTypes.GetValueOrDefault(block.UnlocalizedName);

    /// <summary>
    /// The network id of a block entity type, or -1 when it's unknown.
    /// </summary>
    public static int BlockEntityTypeId(string type) => data.Value.BlockEntityTypeIds.GetValueOrDefault(type, -1);

    /// <summary>
    /// Vanilla <c>isFaceSturdy(..., face, SupportType.FULL)</c>.
    /// </summary>
    public static bool IsFaceSturdy(this IBlock block, BlockFace face) => Has(block, 11 + (int)face);

    /// <summary>
    /// Vanilla <c>isFaceSturdy(..., UP, SupportType.CENTER)</c>.
    /// </summary>
    public static bool IsTopCenterSturdy(this IBlock block) => Has(block, 7);

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

        return data.Value.FaceOcclusion.GetOrAdd((from.StateId(), to.StateId(), direction),
            key => CoversFace(CollisionFace(from, key.Direction), CollisionFace(to, key.Direction.Opposite())));
    }

    // The boxes (min and max on each of the face's two axes) where the collision shape touches the given face.
    private static double[][] CollisionFace(IBlock block, BlockFace face)
    {
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
        return (uint)stateId < (uint)flags.Length ? flags[stateId] : 0;
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
        return (uint)id < (uint)flags.Length ? flags[id] : 0;
    }

    private static int Flags(IBlock block) => Flags(block.GetHashCode());

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
                .ToArray());
    }

    private sealed record PhysicsData(int[] Flags, Dictionary<string, string> BlockClasses, Dictionary<string, string> BlockEntityTypes,
        Dictionary<string, int> BlockEntityTypeIds, HashSet<string> SignalSources, int[] FluidFlags, int[][] CollisionFaceSets,
        double[][][] CollisionFaces)
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
