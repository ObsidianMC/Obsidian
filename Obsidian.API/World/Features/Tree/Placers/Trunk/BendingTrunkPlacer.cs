using System.ComponentModel.DataAnnotations;

namespace Obsidian.API.World.Features.Tree.Placers.Trunk;
public sealed class BendingTrunkPlacer : TrunkPlacer
{
    public override string Type => "bending_trunk_placer";

    [Range(1, 64)]
    public required IIntProvider BendLength { get; init; }

    public int MinHeightForLeaves { get; init; } = 1;
}
