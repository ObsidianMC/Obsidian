namespace Obsidian.WorldData.BlockPredicates;

/// <summary>
/// Matches when the block's fluid is one of <see cref="Fluids"/> (<c>minecraft:empty</c> matches no fluid).
/// </summary>
[ConfiguredFeatureProperty("minecraft:matching_fluids")]
public sealed class MatchingFluidsPredicate : IBlockPredicate
{
    public string Type { get; init; } = "minecraft:matching_fluids";

    public Vector Offset { get; init; } = Vector.Zero;

    /// <summary>
    /// Fluid ids or fluid tags (<c>#minecraft:water</c>).
    /// </summary>
    public required string[] Fluids { get; init; }

    private HashSet<FluidKind> Kinds => field ??= [.. this.Fluids.SelectMany(Resolve)];

    public bool Test(IWorldGenLevel level, Vector position) => this.Kinds.Contains(level.GetBlock(position + this.Offset).GetFluid());

    private static IEnumerable<FluidKind> Resolve(string fluid) => fluid switch
    {
        "minecraft:empty" => [FluidKind.Empty],
        "minecraft:water" => [FluidKind.Water],
        "minecraft:flowing_water" => [FluidKind.FlowingWater],
        "minecraft:lava" => [FluidKind.Lava],
        "minecraft:flowing_lava" => [FluidKind.FlowingLava],
        "#minecraft:water" => [FluidKind.Water, FluidKind.FlowingWater],
        "#minecraft:lava" => [FluidKind.Lava, FluidKind.FlowingLava],
        _ => throw new InvalidOperationException($"Unknown fluid '{fluid}'.")
    };
}
