namespace Obsidian.API.World.Features.PlacementModifiers;

/// <summary>
/// Either returns the current position or empty.
/// Only passes if the biome at the current position includes this placed feature. 
/// No additional field. 
/// </summary>
/// <remarks>
/// This modifier type cannot be used in placed features that are referenced from other configured features.
/// </remarks>
public sealed class BiomePlacement : PlacementModifierBase
{
    public override string Type => "minecraft:biome";

    protected override bool ShouldPlace(PlacementContext context) => throw new NotImplementedException();
}
