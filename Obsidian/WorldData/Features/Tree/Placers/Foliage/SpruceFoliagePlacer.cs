using Obsidian.API.World.Features.Tree;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

[TreeProperty("minecraft:spruce_foliage_placer")]
public sealed class SpruceFoliagePlacer : FoliagePlacer
{
    public override required string Type { get; init; } = "minecraft:spruce_foliage_placer";

    public required IIntProvider TrunkHeight { get; set; }
}
