using Obsidian.API.World.Features.Tree;
using System.ComponentModel.DataAnnotations;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

[TreeProperty("minecraft:blob_foliage_placer")]
[TreeProperty("minecraft:bush_foliage_placer")]
[TreeProperty("minecraft:fancy_foliage_placer")]
[TreeProperty("minecraft:jungle_foliage_placer")]
public sealed class HeightFoliagePlacer : FoliagePlacer
{
    public override required string Type { get; init; }

    [Range(0, 16)]
    public required IIntProvider Height { get; init; }
}
