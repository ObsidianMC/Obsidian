namespace Obsidian.API.World.Features.PlacementModifiers;

/// <summary>
/// Returns all positions in the current chunk that have been carved out by a carver. 
/// This does not include blocks carved out by noise caves.
/// </summary>
public sealed class CarvingMaskPlacement : PlacementModifierBase
{
    public override string Type => "minecraft:carving_mask";

    /// <summary>
    /// The carving step. Either air or liquid.
    /// </summary>
    public required string Step { get; init; }

    protected override bool ShouldPlace(PlacementContext context) => throw new NotImplementedException();
}
