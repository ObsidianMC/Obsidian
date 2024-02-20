using System.ComponentModel.DataAnnotations;

namespace Obsidian.WorldData.Features.PlacementModifiers;

/// <summary>
/// Returns multiple copies of the current block position
/// </summary>
[TreeProperty("minecraft:count")]
public sealed class CountPlacement : PlacementModifierBase
{
    public override string Type { get; internal init; } = "minecraft:count";

    /// <summary>
    /// Value between 0 and 256 (inclusive).
    /// </summary>
    [Range(0, 256)]
    public required IIntProvider Count { get; init; }

    protected override bool ShouldPlace(PlacementContext context) => throw new NotImplementedException();
}
