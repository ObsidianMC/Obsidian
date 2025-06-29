using Obsidian.API.World.Features.Tree;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

[TreeProperty("minecraft:acacia_foliage_placer")]
[TreeProperty("minecraft:dark_oak_foliage_placer")]
public sealed class DefaultFoliagePlacer : FoliagePlacer
{
    public required override string Type { get; init; }
}
