using System.ComponentModel.DataAnnotations;

namespace Obsidian.API.World.Features.Tree.Placers.Foliage;
public sealed class CherryFoliagePlacer : FoliagePlacer
{
    public override string Type => "cherry_foliage_placer";

    [Range(4, 16)]
    public override required IIntProvider Height { get; set; }

    [Range(0.0, 1.0)]
    public required float WideBottomLayerHoleChance { get; set; }

    [Range(0.0, 1.0)]
    public required float CornerHoleChance { get; set; }

    [Range(0.0, 1.0)]
    public required float HangingLeavesChance { get; set; }

    [Range(0.0, 1.0)]
    public required float HangingLeavesExtensionChance { get; set; }
}
