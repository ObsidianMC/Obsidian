namespace Obsidian.API.World.Features.PlacementModifiers;

/// <summary>
/// Sets the Y coordinate to a value provided by a height provider. 
/// Returns the new position.
/// </summary>
public sealed class HeightRangePlacement : PlacementModifierBase
{
    public override string Type => "minecraft:height_range";

    /// <summary>
    /// The new Y coordinate.
    /// </summary>
    public required IHeightProvider HeightProvider { get; init; }

    protected override bool ShouldPlace(PlacementContext context) => throw new NotImplementedException();
}
