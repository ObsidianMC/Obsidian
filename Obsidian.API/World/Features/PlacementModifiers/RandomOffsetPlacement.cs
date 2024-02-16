using System.ComponentModel.DataAnnotations;

namespace Obsidian.API.World.Features.PlacementModifiers;
/// <summary>
/// Applies an offset to the current position.
/// </summary>
/// <remarks>
/// Note that the even though X and Z share the same int provider, they are individually sampled, so a different offset can be applied to X and Z.
/// </remarks>
public sealed class RandomOffsetPlacement : PlacementModifierBase
{
    public override string Type => "minecraft:random_offset";

    [Range(-16, 16)]
    public required IIntProvider XZSpread { get; init; }

    [Range(-16, 16)]
    public required IIntProvider YSpread { get; init; }

    protected override bool ShouldPlace(PlacementContext context) => throw new NotImplementedException();
}
