using Obsidian.API.World.Features.Tree;
using System.ComponentModel.DataAnnotations;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

[TreeProperty("minecraft:mega_pine_foliage_placer")]
public sealed class MegaPineFoliagePlacer : FoliagePlacer
{
    public override required string Type { get; init; } = "minecraft:mega_pine_foliage_placer";

    [Range(0, 24)]
    public required IIntProvider CrownHeight { get; set; }
}
