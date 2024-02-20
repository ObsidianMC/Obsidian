using Obsidian.API.World.Features.Tree;

namespace Obsidian.WorldData.Features.Tree.Placers.Trunk;

[TreeProperty("minecraft:forking_trunk_placer")]
[TreeProperty("minecraft:straight_trunk_placer")]
[TreeProperty("minecraft:giant_trunk_placer")]
[TreeProperty("minecraft:mega_jungle_trunk_placer")]
[TreeProperty("minecraft:dark_oak_trunk_placer")]
[TreeProperty("minecraft:fancy_trunk_placer")]
public sealed class DefaultTrunkPlacer : TrunkPlacer
{
    public override required string Type { get; init; }
}
