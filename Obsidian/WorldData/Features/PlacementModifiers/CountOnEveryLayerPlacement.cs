using System.ComponentModel.DataAnnotations;

namespace Obsidian.WorldData.Features.PlacementModifiers;

/// <summary>
/// In the horizontal relative range (0,0) to (16,16), 
/// at each vertical layer separated by air, lava or water, 
/// tries to randomly select the specified number of horizontal positions, 
/// whose Y coordinate is one block above this layer at this selected horizontal position. 
/// Return these selected positions.
/// </summary>
[TreeProperty("minecraft:count_on_every_layer")]
public sealed class CountOnEveryLayerPlacement : PlacementModifierBase
{
    public override string Type { get; internal init; } = "minecraft:count_on_every_layer";

    /// <summary>
    /// Count on each layer. Value between 0 and 256 (inclusive).
    /// </summary>
    [Range(0, 256)]
    public required IIntProvider Count { get; init; }

    protected override bool ShouldPlace(PlacementContext context) => throw new NotImplementedException();
}
