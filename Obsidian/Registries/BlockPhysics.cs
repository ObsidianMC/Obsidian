using System.Reflection;
using System.Text.Json;

namespace Obsidian.Registries;

/// <summary>
/// Vanilla's per-state block physics (collision, replaceability, fluids, sturdy faces), used by world generation.
/// </summary>
/// <remarks>
/// Loaded from <c>Assets/block_physics.json</c>, which was dumped from vanilla 1.21.11 for every block state, along with
/// each block's vanilla class and block entity type.
/// Each state packs its flags into one int:
/// bit 0 air, 1 blocks motion, 2 solid, 3 replaceable, 4 liquid, 5 fluid source, 6 full collision cube,
/// 7 sturdy center (up), 8 sturdy rigid (up), 9 redstone conductor, 10 block entity, 11-16 sturdy full faces
/// (one bit per <see cref="BlockFace"/>), 17-20 light emission, 21-24 fluid amount, 25-27 fluid kind, 28 empty collision,
/// 29 full top collision face.
/// </remarks>
internal static class BlockPhysics
{
    private static readonly Lazy<PhysicsData> data = new(Load);

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

    private static bool Has(IBlock block, int bit) => (Flags(block) & (1 << bit)) != 0;

    private static int Flags(IBlock block)
    {
        var flags = data.Value.Flags;
        var id = block.GetHashCode();
        return (uint)id < (uint)flags.Length ? flags[id] : 0;
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
            root.GetProperty("blockEntityTypeIds").EnumerateObject().ToDictionary(entry => entry.Name, entry => entry.Value.GetInt32()));
    }

    private sealed record PhysicsData(int[] Flags, Dictionary<string, string> BlockClasses, Dictionary<string, string> BlockEntityTypes,
        Dictionary<string, int> BlockEntityTypeIds);
}

/// <summary>
/// Fluid carried by a block state, in vanilla's fluid registry terms.
/// </summary>
internal enum FluidKind
{
    Empty,
    Water,
    FlowingWater,
    Lava,
    FlowingLava
}
