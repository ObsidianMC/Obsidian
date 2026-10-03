namespace Obsidian.WorldData.Features.Tree.Placers.Trunk;

/// <summary>
/// Mangrove trunk: a straight trunk that randomly sprouts diagonal branches climbing outward, each carrying foliage.
/// </summary>
[ConfiguredFeatureProperty("minecraft:upwards_branching_trunk_placer")]
public sealed class UpwardsBranchingTrunkPlacer : TrunkPlacer
{
    public required IIntProvider ExtraBranchSteps { get; init; }

    public required float PlaceBranchPerLogProbability { get; init; }

    public required IIntProvider ExtraBranchLength { get; init; }

    /// <summary>Blocks the trunk may replace in addition to air and <c>#replaceable_by_trees</c>.</summary>
    public required BlockSet CanGrowThrough { get; init; }

    public override List<FoliageAttachment> PlaceTrunk(TreeContext tree, int freeTreeHeight, Vector origin)
    {
        var random = tree.Random;
        var attachments = new List<FoliageAttachment>();
        for (var i = 0; i < freeTreeHeight; i++)
        {
            var y = origin.Y + i;
            var position = new Vector(origin.X, y, origin.Z);
            if (this.PlaceLog(tree, position) && i < freeTreeHeight - 1 && random.NextFloat() < this.PlaceBranchPerLogProbability)
            {
                var direction = TreeDirections.RandomHorizontal(random);
                var length = this.ExtraBranchLength.Sample(random);
                var branchStart = Math.Max(0, length - this.ExtraBranchLength.Sample(random) - 1);
                var steps = this.ExtraBranchSteps.Sample(random);
                this.PlaceBranch(tree, freeTreeHeight, attachments, position, y, direction, branchStart, steps);
            }

            if (i == freeTreeHeight - 1)
                attachments.Add(new FoliageAttachment(new Vector(origin.X, y + 1, origin.Z), 0, false));
        }

        return attachments;
    }

    protected override bool ValidTreePos(IWorldGenLevel level, Vector position) =>
        base.ValidTreePos(level, position) || this.CanGrowThrough.Contains(level.GetBlock(position));

    private void PlaceBranch(TreeContext tree, int freeTreeHeight, List<FoliageAttachment> attachments, Vector trunkPosition, int trunkY,
        BlockFace direction, int branchStart, int steps)
    {
        var topY = trunkY + branchStart;
        var x = trunkPosition.X;
        var z = trunkPosition.Z;
        var step = direction.ToVector();

        for (var i = branchStart; i < freeTreeHeight && steps > 0; i++, steps--)
        {
            if (i < 1)
                continue;

            var y = trunkY + i;
            x += step.X;
            z += step.Z;
            topY = y;
            var position = new Vector(x, y, z);
            if (this.PlaceLog(tree, position))
                topY++;

            attachments.Add(new FoliageAttachment(position, 0, false));
        }

        if (topY - trunkY > 1)
        {
            var end = new Vector(x, topY, z);
            attachments.Add(new FoliageAttachment(end, 0, false));
            attachments.Add(new FoliageAttachment(end + (0, -2, 0), 0, false));
        }
    }
}
