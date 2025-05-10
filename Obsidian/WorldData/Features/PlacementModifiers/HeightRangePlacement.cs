namespace Obsidian.WorldData.Features.PlacementModifiers;

/// <summary>
/// Sets the Y coordinate to a value provided by a height provider. 
/// Returns the new position.
/// </summary>
[TreeProperty("minecraft:height_range")]
public sealed class HeightRangePlacement : PlacementModifierBase
{
    public override string Type { get; internal init; } = "minecraft:height_range";

    /// <summary>
    /// The new Y coordinate.
    /// </summary>
    public required IHeightProvider HeightProvider { get; init; }

    protected override bool ShouldPlace(PlacementContext context) => throw new NotImplementedException();
}
