namespace Obsidian.API.World.Features.PlacementModifiers;

/// <summary>
/// Either returns the current position or empty. 
/// The chance is calculated by 1 / chance.
/// </summary>
public sealed class RarityFilterPlacement : PlacementModifierBase
{
    public override string Type => "minecraft:rarity_filter";

    /// <remarks>
    /// Must be a positive integer.
    /// </remarks>
    public required int Chance { get; init; }

    protected override bool ShouldPlace(PlacementContext context) => throw new NotImplementedException();
}
