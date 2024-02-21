using Obsidian.API.World.Features.Tree;
using System.ComponentModel.DataAnnotations;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

[TreeProperty("minecraft:pine_foliage_placer")]
public sealed class PineFoliagePlacer : FoliagePlacer
{
    public override required string Type { get; init; } = "minecraft:pine_foliage_placer";

    [Range(0, 24)]
    public IIntProvider Height { get; set; } = default!;
}
