namespace Obsidian.WorldData.Features;

/// <summary>
/// Replaces enclosed floor blocks under water with magma, like vanilla's UnderwaterMagmaFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:underwater_magma")]
public sealed class UnderwaterMagmaFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:underwater_magma";

    /// <summary>
    /// How far down the floor is searched through water.
    /// </summary>
    public required int FloorSearchRange { get; init; }

    public required int PlacementRadiusAroundFloor { get; init; }

    public required float PlacementProbabilityPerValidPosition { get; init; }

    private static IBlock Magma => field ??= BlocksRegistry.Get(Material.MagmaBlock);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin))
            return false;

        var column = Column.Scan(level, origin, this.FloorSearchRange, block => block.Material == Material.Water,
            block => block.Material != Material.Water);
        if (column?.Floor is not int floor)
            return false;

        var center = origin.AtY(floor);
        var radius = new Vector(this.PlacementRadiusAroundFloor);
        var placed = 0;

        // Vanilla streams the box: the probability roll comes first and the validity check only runs for winners.
        foreach (var position in FeatureHelpers.BetweenClosed(center - radius, center + radius))
        {
            if (random.NextFloat() >= this.PlacementProbabilityPerValidPosition || !IsValidPlacement(level, position))
                continue;

            level.SetBlock(position, Magma);
            placed++;
        }

        return placed > 0;
    }

    private static bool IsValidPlacement(IWorldGenLevel level, Vector position)
    {
        var block = level.GetBlock(position);
        if (block.Material == Material.Water || block.IsAir || IsVisibleFromOutside(level, position + Vector.Down))
            return false;

        foreach (var face in FeatureHelpers.Horizontal)
        {
            if (IsVisibleFromOutside(level, position.Offset(face)))
                return false;
        }

        return true;
    }

    /// <remarks>
    /// Vanilla checks that the neighbor's face occlusion shape toward us isn't a full face. Obsidian has no occlusion data,
    /// so a neighbor hides the position when it renders as a solid cube (<see cref="FeatureHelpers.IsSolidRender"/>).
    /// </remarks>
    private static bool IsVisibleFromOutside(IWorldGenLevel level, Vector position) => !FeatureHelpers.IsSolidRender(level.GetBlock(position));
}
