using System.ComponentModel.DataAnnotations;

namespace Obsidian.WorldData.Features.PlacementModifiers;

/// <summary>
/// Scans blocks either up or down, until the target condition is met. 
/// Returns the block position for which the target condition matches.
/// If no target can be found within the maximum number of steps, returns empty.
/// </summary>
[TreeProperty("minecraft:environment_scan")]
public sealed class EnviromentScanPlacement : PlacementModifierBase
{
    public override string Type { get; internal init; } = "minecraft:environment_scan";

    /// <summary>
    /// Can be either up or down.
    /// </summary>
    public required string DirectionOfSearch { get; init; }

    [Range(1, 32)]
    public required int MaxSteps { get; init; }

    /// <summary>
    /// The block predicate that is searched for.
    /// </summary>
    public required IBlockPredicate TargetCondition { get; init; }

    /// <summary>
    /// If specified, each step must match this block position in order to continue the scan. 
    /// If a block that doesn't match it is met, but no target block found, returns empty.
    /// </summary>
    public IBlockPredicate? AllowedSearchCondition { get; init; }

    protected override bool ShouldPlace(PlacementContext context) => throw new NotImplementedException();
}
