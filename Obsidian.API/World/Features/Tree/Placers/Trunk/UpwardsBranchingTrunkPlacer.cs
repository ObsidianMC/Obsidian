﻿using System.ComponentModel.DataAnnotations;

namespace Obsidian.API.World.Features.Tree.Placers;
public sealed class UpwardsBranchingTrunkPlacer : TrunkPlacer
{
    public override string Type => "upwards_branching_trunk_placer";

    public required IIntProvider ExtraBranchSteps { get; init; }

    public required IIntProvider ExtraBranchLength { get; init; }

    [Range(0.0, 1.0)]
    public required float PlaceBranchPerLogProbability { get; init; }

    public List<string> CanGrowThrough { get; } = [];
}
