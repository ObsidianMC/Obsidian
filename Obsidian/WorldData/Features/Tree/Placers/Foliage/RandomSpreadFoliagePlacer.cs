using Obsidian.API.World.Features.Tree;
using System.ComponentModel.DataAnnotations;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

[TreeProperty("minecraft:random_spread_foliage_placer")]
public sealed class RandomSpreadFoliagePlacer : FoliagePlacer
{
    public override required string Type { get; init; } = "minecraft:random_spread_foliage_placer";

    [Range(1, 512)]
    public required IIntProvider FoliageHeight { get; set; }

    [Range(0, 256)]
    public required int LeafPlacementAttempts { get; set; }
}
