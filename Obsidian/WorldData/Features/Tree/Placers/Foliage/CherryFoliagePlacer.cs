using Obsidian.API.World.Features.Tree;
using System.ComponentModel.DataAnnotations;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

[TreeProperty("minecraft:cherry_foliage_placer")]
public sealed class CherryFoliagePlacer : FoliagePlacer
{
    public override required string Type { get; init; } = "minecraft:cherry_foliage_placer";

    [Range(4, 16)]
    public required IIntProvider Height { get; set; }

    [Range(0.0, 1.0)]
    public required float WideBottomLayerHoleChance { get; set; }

    [Range(0.0, 1.0)]
    public required float CornerHoleChance { get; set; }

    [Range(0.0, 1.0)]
    public required float HangingLeavesChance { get; set; }

    [Range(0.0, 1.0)]
    public required float HangingLeavesExtensionChance { get; set; }
}
