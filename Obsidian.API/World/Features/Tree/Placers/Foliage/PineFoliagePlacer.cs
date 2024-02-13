using System.ComponentModel.DataAnnotations;

namespace Obsidian.API.World.Features.Tree.Placers.Foliage;
public sealed class PineFoliagePlacer : FoliagePlacer
{
    public override string Type => "pine_foliage_placer";

    [Range(0, 24)]
    public override IIntProvider Height { get; set; } = default!;
}
