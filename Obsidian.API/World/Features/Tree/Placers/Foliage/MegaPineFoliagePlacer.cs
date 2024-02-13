using System.ComponentModel.DataAnnotations;

namespace Obsidian.API.World.Features.Tree.Placers.Foliage;
public sealed class MegaPineFoliagePlacer : FoliagePlacer
{
    public override string Type => "mega_pine_foliage_placer";

    [Range(0, 24)]
    public required IIntProvider CrownHeight { get; set; }
}
