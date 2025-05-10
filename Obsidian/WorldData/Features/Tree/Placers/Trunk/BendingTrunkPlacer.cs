using Obsidian.API.World.Features.Tree;
using System.ComponentModel.DataAnnotations;

namespace Obsidian.WorldData.Features.Tree.Placers.Trunk;

[TreeProperty("minecraft:bending_trunk_placer")]
public sealed class BendingTrunkPlacer : TrunkPlacer
{
    public override string Type { get; init; } = "minecraft:bending_trunk_placer";

    [Range(1, 64)]
    public required IIntProvider BendLength { get; init; }

    public int MinHeightForLeaves { get; init; } = 1;
}
