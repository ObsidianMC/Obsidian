using System.ComponentModel.DataAnnotations;

namespace Obsidian.API.World.Features.Tree.Placers.Trunk;
public sealed class CherryTrunkPlacer : TrunkPlacer
{
    public override string Type => "cherry_trunk_placer";

    [Range(1, 3)]
    public required IIntProvider BranchCount { get; init; }

    [Range(2, 16)]
    public required IIntProvider BranchHorizontalLength { get; init; }

    [Range(-16, 0)]
    public required IntProviderRangeValue BranchStartOffsetFromTop { get; init; }

    [Range(-16, 16)]
    public required IIntProvider BranchEndOffsetFromTop { get; init; }
}
