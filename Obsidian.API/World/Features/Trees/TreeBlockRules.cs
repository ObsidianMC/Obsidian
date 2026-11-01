using Obsidian.API.Registries;
using Obsidian.API.World.Generator;

namespace Obsidian.API.World.Features.Trees;

/// <summary>
/// The block tests and changes tree parts make, like vanilla's <c>TreeFeature</c>, <c>TrunkPlacer</c> and
/// <c>RootPlacer</c> helpers.
/// </summary>
internal static class TreeBlockRules
{
    public static readonly WorldgenTags.BlockGroup Logs = WorldgenTags.Blocks("logs");

    private static readonly WorldgenTags.BlockGroup replaceableByTrees = WorldgenTags.Blocks("replaceable_by_trees");

    private static readonly WorldgenTags.BlockGroup dirt = WorldgenTags.Blocks("dirt");

    private static readonly BlockSet grassOrMycelium = new("minecraft:grass_block", "minecraft:mycelium");

    /// <summary>
    /// Reads and changes block states, which only the server can: it sets this at startup, like
    /// <see cref="SimpleBlockStateExtensions"/>' converter.
    /// </summary>
    public static IBlockStateAccess States
    {
        get => field ?? throw new InvalidOperationException("Block state access isn't initialized.");
        set;
    }

    /// <summary>The default vine state, with no faces attached.</summary>
    public static IBlock Vine => field ??= new SimpleBlockState { Name = "minecraft:vine" }.ToBlock();

    /// <summary>Vanilla <c>TreeFeature.validTreePos</c>: air or <c>#replaceable_by_trees</c>.</summary>
    public static bool ValidTreePos(IWorldGenLevel level, Vector position)
    {
        var block = level.GetBlock(position);
        return block.IsAir || replaceableByTrees.Contains(block);
    }

    /// <summary>Vanilla <c>TrunkPlacer.isDirt</c>: <c>#dirt</c> except grass blocks and mycelium.</summary>
    public static bool IsDirtUnderTrunk(IBlock block) => dirt.Contains(block) && !grassOrMycelium.Contains(block);

    // Not extension methods: Obsidian's own block state extensions have these names.
    public static string? GetProperty(IBlock block, string property) => States.GetProperty(block, property);

    public static bool HasProperty(IBlock block, string property) => GetProperty(block, property) is not null;

    /// <summary>The state with the property set, or the block itself when it has no such property or value.</summary>
    public static IBlock WithProperty(IBlock block, string property, string value) =>
        States.WithProperty(block, property, value);

    public static IBlock WithProperty(IBlock block, string property, bool value) =>
        WithProperty(block, property, value ? "true" : "false");
}

/// <summary>
/// Block state access tree parts need from the server.
/// </summary>
internal interface IBlockStateAccess
{
    string? GetProperty(IBlock block, string property);

    IBlock WithProperty(IBlock block, string property, string value);

    /// <summary>Vanilla <c>FluidState.isSourceOfType(Fluids.WATER)</c>: the block holds a water source.</summary>
    bool IsWaterSource(IBlock block);

    /// <summary>Vanilla <c>FluidState.is(FluidTags.WATER)</c>: the block holds still or flowing water.</summary>
    bool IsWater(IBlock block);
}
