namespace Obsidian.WorldData.Features.PlacementModifiers;

/// <summary>
/// Either returns the current position or empty. 
/// The chance is calculated by 1 / chance.
/// </summary>
[TreeProperty("minecraft:rarity_filter")]
public sealed class RarityFilterPlacement : PlacementModifierBase
{
    public override string Type { get; internal init; } = "minecraft:rarity_filter";

    /// <remarks>
    /// Must be a positive integer.
    /// </remarks>
    public required int Chance { get; init; }

    protected override bool ShouldPlace(PlacementContext context) => throw new NotImplementedException();
}
