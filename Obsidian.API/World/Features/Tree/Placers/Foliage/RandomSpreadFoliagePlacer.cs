using System.ComponentModel.DataAnnotations;

namespace Obsidian.API.World.Features.Tree.Placers.Foliage;
public sealed class RandomSpreadFoliagePlacer : FoliagePlacer
{
    public override string Type => "random_spread_foliage_placer";

    [Range(1, 512)]
    public required IIntProvider FoliageHeight { get; set; }

    [Range(0, 256)]
    public required int LeafPlacementAttempt { get; set; }
}
