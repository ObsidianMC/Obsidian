using Obsidian.Providers.IntProviders;

namespace Obsidian.WorldData.Features.Tree.Placers.Trunk;

/// <summary>
/// Cherry trunk: a short trunk with one to three branches (two opposite branches, optionally the trunk itself) that
/// wander outward and upward toward their foliage.
/// </summary>
[ConfiguredFeatureProperty("minecraft:cherry_trunk_placer")]
public sealed class CherryTrunkPlacer : TrunkPlacer
{
    public required IIntProvider BranchCount { get; init; }

    public required IIntProvider BranchHorizontalLength { get; init; }

    /// <summary>Always a plain uniform range in vanilla (the JSON has no <c>type</c>).</summary>
    public required UniformIntProvider BranchStartOffsetFromTop { get; init; }

    public required IIntProvider BranchEndOffsetFromTop { get; init; }

    public override List<FoliageAttachment> PlaceTrunk(TreeContext tree, int freeTreeHeight, Vector origin)
    {
        var random = tree.Random;
        SetDirtAt(tree, origin + Vector.Down);

        var firstBranchStart = Math.Max(0, freeTreeHeight - 1 + this.BranchStartOffsetFromTop.Sample(random));

        // The second branch samples one block less of the range and skips over the first branch's start.
        var start = this.BranchStartOffsetFromTop;
        var secondOffset = random.NextInt(start.MaxInclusive - 1 - start.MinInclusive + 1) + start.MinInclusive;
        var secondBranchStart = Math.Max(0, freeTreeHeight - 1 + secondOffset);
        if (secondBranchStart >= firstBranchStart)
            secondBranchStart++;

        var branchCount = this.BranchCount.Sample(random);
        var hasMiddleBranch = branchCount == 3;
        var hasSecondBranch = branchCount >= 2;
        int trunkHeight;
        if (hasMiddleBranch)
            trunkHeight = freeTreeHeight;
        else if (hasSecondBranch)
            trunkHeight = Math.Max(firstBranchStart, secondBranchStart) + 1;
        else
            trunkHeight = firstBranchStart + 1;

        for (var y = 0; y < trunkHeight; y++)
            this.PlaceLog(tree, origin + (0, y, 0));

        var attachments = new List<FoliageAttachment>();
        if (hasMiddleBranch)
            attachments.Add(new FoliageAttachment(origin + (0, trunkHeight, 0), 0, false));

        var direction = TreeDirections.RandomHorizontal(random);
        var horizontalLog = WithAxis(AxisOf(direction));
        attachments.Add(this.GenerateBranch(tree, freeTreeHeight, origin, horizontalLog, direction, firstBranchStart,
            firstBranchStart < trunkHeight - 1));
        if (hasSecondBranch)
        {
            attachments.Add(this.GenerateBranch(tree, freeTreeHeight, origin, horizontalLog, direction.Opposite(), secondBranchStart,
                secondBranchStart < trunkHeight - 1));
        }

        return attachments;
    }

    private FoliageAttachment GenerateBranch(TreeContext tree, int freeTreeHeight, Vector origin, Func<IBlock, IBlock> horizontalLog,
        BlockFace direction, int branchStart, bool middleContinuesAbove)
    {
        var random = tree.Random;
        var cursor = origin + (0, branchStart, 0);
        var endY = freeTreeHeight - 1 + this.BranchEndOffsetFromTop.Sample(random);
        var extendedStart = middleContinuesAbove || endY < branchStart;
        var horizontalLength = this.BranchHorizontalLength.Sample(random) + (extendedStart ? 1 : 0);
        var step = direction.ToVector();
        var end = origin + step * horizontalLength + (0, endY, 0);

        var startLogs = extendedStart ? 2 : 1;
        for (var i = 0; i < startLogs; i++)
        {
            cursor += step;
            this.PlaceLog(tree, cursor, horizontalLog);
        }

        var vertical = end.Y > cursor.Y ? Vector.Up : Vector.Down;
        while (true)
        {
            var distance = TreeDirections.DistManhattan(cursor, end);
            if (distance == 0)
                return new FoliageAttachment(end + Vector.Up, 0, false);

            // Move vertically with probability proportional to the remaining vertical distance.
            var verticalChance = (float)Math.Abs(end.Y - cursor.Y) / distance;
            var moveVertically = random.NextFloat() < verticalChance;
            cursor += moveVertically ? vertical : step;
            this.PlaceLog(tree, cursor, moveVertically ? null : horizontalLog);
        }
    }
}
