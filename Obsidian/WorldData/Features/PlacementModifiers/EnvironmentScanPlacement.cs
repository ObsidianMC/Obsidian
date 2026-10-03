using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.PlacementModifiers;

/// <summary>
/// Walks from the position in <see cref="DirectionOfSearch"/> until <see cref="TargetCondition"/> matches,
/// while <see cref="AllowedSearchCondition"/> holds, for at most <see cref="MaxSteps"/> steps.
/// </summary>
[ConfiguredFeatureProperty("minecraft:environment_scan")]
public sealed class EnvironmentScanPlacement : SinglePlacementModifierBase
{
    public override string Type => "minecraft:environment_scan";

    public required BlockFace DirectionOfSearch { get; init; }

    public required IBlockPredicate TargetCondition { get; init; }

    public IBlockPredicate AllowedSearchCondition { get; init; } = new BlockPredicates.TruePredicate();

    public required int MaxSteps { get; init; }

    public override Vector? GetPosition(PlacementContext context, IRandomSource random, Vector position)
    {
        var level = context.Level;
        var step = this.DirectionOfSearch.ToVector();
        var current = position;

        if (!this.AllowedSearchCondition.Test(level, current))
            return null;

        for (var i = 0; i < this.MaxSteps; i++)
        {
            if (this.TargetCondition.Test(level, current))
                return current;

            current += step;
            if (level.IsOutsideBuildHeight(current.Y))
                return null;

            if (!this.AllowedSearchCondition.Test(level, current))
                break;
        }

        return this.TargetCondition.Test(level, current) ? current : null;
    }
}
