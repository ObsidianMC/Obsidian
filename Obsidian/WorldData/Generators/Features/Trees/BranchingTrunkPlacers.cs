using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features.Trees;

/// <summary>Fancy oak: limbs to foliage clusters around a trunk ending about 0.618 of the way up.</summary>
internal sealed class FancyTrunkPlacer(JsonElement json) : TrunkPlacer(json)
{
    private const double TrunkHeightScale = 0.618;
    private const double ClusterDensityMagic = 1.382;
    private const double BranchSlope = 0.381;
    private const double BranchLengthMagic = 0.328;

    public override List<FoliageAttachment> PlaceTrunk(TreePlacement tree, int freeTreeHeight, Vector origin)
    {
        var random = tree.Random;
        var height = freeTreeHeight + 2;
        var trunkHeight = Mth.Floor(height * TrunkHeightScale);

        tree.PlaceBelowTrunk(origin + Vector.Down);

        var density = ClusterDensityMagic + Math.Pow(1.0 * height / 13.0, 2.0);
        var clustersPerY = Math.Min(1, Mth.Floor(density));
        var trunkTop = origin.Y + trunkHeight;
        var relativeY = height - 5;
        var topCluster = new FoliageAttachment(origin + (0, relativeY, 0), 0, false);
        var clusters = new List<(FoliageAttachment Attachment, int BranchBase)> { (topCluster, trunkTop) };

        for (; relativeY >= 0; relativeY--)
        {
            var shape = TreeShape(height, relativeY);

            if (shape < 0.0f)
            {
                continue;
            }

            for (var i = 0; i < clustersPerY; i++)
            {
                var cluster = this.TryCluster(tree, origin, shape, relativeY, trunkTop);

                if (cluster is not null)
                {
                    clusters.Add(cluster.Value);
                }
            }
        }

        this.MakeLimb(tree, origin, origin + (0, trunkHeight, 0), true);
        this.MakeBranches(tree, height, origin, clusters);

        var attachments = new List<FoliageAttachment>();

        foreach (var (attachment, branchBase) in clusters)
        {
            if (TrimBranches(height, branchBase - origin.Y))
            {
                attachments.Add(attachment);
            }
        }

        return attachments;
    }

    // A cluster at a random angle and distance, if its limb and the branch back to the trunk are free.
    private (FoliageAttachment, int)? TryCluster(
        TreePlacement tree, Vector origin, float shape, int relativeY, int trunkTop)
    {
        var random = tree.Random;
        var radius = 1.0 * shape * (random.NextFloat() + BranchLengthMagic);
        var angle = random.NextFloat() * 2.0f * Math.PI;
        var offsetX = radius * Math.Sin(angle) + 0.5;
        var offsetZ = radius * Math.Cos(angle) + 0.5;
        var clusterBase = origin + (Mth.Floor(offsetX), relativeY - 1, Mth.Floor(offsetZ));
        var clusterTop = clusterBase + (0, 5, 0);

        if (!this.MakeLimb(tree, clusterBase, clusterTop, false))
        {
            return null;
        }

        var dx = origin.X - clusterBase.X;
        var dz = origin.Z - clusterBase.Z;
        var branchY = clusterBase.Y - Math.Sqrt(dx * dx + dz * dz) * BranchSlope;
        var branchBaseY = branchY > trunkTop ? trunkTop : (int)branchY;
        var branchBase = new Vector(origin.X, branchBaseY, origin.Z);

        if (!this.MakeLimb(tree, branchBase, clusterBase, false))
        {
            return null;
        }

        return (new FoliageAttachment(clusterBase, 0, false), branchBase.Y);
    }

    // Walks from start to end in float steps: places logs along it, or checks that every block is free.
    private bool MakeLimb(TreePlacement tree, Vector start, Vector end, bool place)
    {
        if (!place && start == end)
        {
            return true;
        }

        var delta = end - start;
        var steps = Math.Max(Math.Abs(delta.X), Math.Max(Math.Abs(delta.Y), Math.Abs(delta.Z)));
        var stepX = (float)delta.X / steps;
        var stepY = (float)delta.Y / steps;
        var stepZ = (float)delta.Z / steps;

        for (var i = 0; i <= steps; i++)
        {
            var offsetX = Mth.Floor(0.5f + i * stepX);
            var offsetY = Mth.Floor(0.5f + i * stepY);
            var offsetZ = Mth.Floor(0.5f + i * stepZ);
            var position = start + (offsetX, offsetY, offsetZ);

            if (place)
            {
                this.PlaceLog(tree, position, WithAxis(LogAxis(start, position)));
            }
            else if (!this.IsFree(tree.Tags, tree.Level, position))
            {
                return false;
            }
        }

        return true;
    }

    private static string LogAxis(Vector start, Vector position)
    {
        var dx = Math.Abs(position.X - start.X);
        var dz = Math.Abs(position.Z - start.Z);
        var max = Math.Max(dx, dz);

        if (max <= 0)
        {
            return "y";
        }

        return dx == max ? "x" : "z";
    }

    private static bool TrimBranches(int height, int branchBaseHeight) => branchBaseHeight >= height * 0.2;

    private void MakeBranches(
        TreePlacement tree, int height, Vector origin, List<(FoliageAttachment Attachment, int BranchBase)> clusters)
    {
        foreach (var (attachment, branchBaseY) in clusters)
        {
            var branchBase = new Vector(origin.X, branchBaseY, origin.Z);
            var kept = TrimBranches(height, branchBaseY - origin.Y);

            if (branchBase != attachment.Position && kept)
            {
                this.MakeLimb(tree, branchBase, attachment.Position, true);
            }
        }
    }

    private static float TreeShape(int height, int y)
    {
        if (y < height * 0.3f)
        {
            return -1.0f;
        }

        var half = height / 2.0f;
        var fromCenter = half - y;
        var radius = MathF.Sqrt(half * half - fromCenter * fromCenter);

        if (fromCenter == 0.0f)
        {
            radius = half;
        }
        else if (Math.Abs(fromCenter) >= half)
        {
            return 0.0f;
        }

        return radius * 0.5f;
    }
}

/// <summary>Cherry: one to three branches curving out and up from a short trunk.</summary>
internal sealed class CherryTrunkPlacer(JsonElement json) : TrunkPlacer(json)
{
    private readonly IntProvider branchCount = ValueProviders.Int(json.GetProperty("branch_count"));
    private readonly IntProvider branchHorizontalLength =
        ValueProviders.Int(json.GetProperty("branch_horizontal_length"));


    private readonly IntProvider branchEndOffset = ValueProviders.Int(json.GetProperty("branch_end_offset_from_top"));

    // The start offset is a bare uniform range (no type): the second branch draws from one less than its range.
    private readonly int startMin = json.GetProperty("branch_start_offset_from_top")
        .GetProperty("min_inclusive")
        .GetInt32();

    private readonly int startMax = json.GetProperty("branch_start_offset_from_top")
        .GetProperty("max_inclusive")
        .GetInt32();

    public override List<FoliageAttachment> PlaceTrunk(TreePlacement tree, int freeTreeHeight, Vector origin)
    {
        var random = tree.Random;
        tree.PlaceBelowTrunk(origin + Vector.Down);

        var firstOffset = random.NextIntBetweenInclusive(this.startMin, this.startMax);
        var firstBranchStart = Math.Max(0, freeTreeHeight - 1 + firstOffset);
        var secondOffset = random.NextInt(this.startMax - 1 - this.startMin + 1) + this.startMin;
        var secondBranchStart = Math.Max(0, freeTreeHeight - 1 + secondOffset);

        if (secondBranchStart >= firstBranchStart)
        {
            secondBranchStart++;
        }

        var branches = this.branchCount(random);
        var hasMiddleBranch = branches == 3;
        var hasSecondBranch = branches >= 2;
        var trunkHeight = TrunkHeight(freeTreeHeight, firstBranchStart, secondBranchStart, branches);

        for (var y = 0; y < trunkHeight; y++)
        {
            this.PlaceLog(tree, origin + (0, y, 0));
        }

        var attachments = new List<FoliageAttachment>();

        if (hasMiddleBranch)
        {
            attachments.Add(new FoliageAttachment(origin + (0, trunkHeight, 0), 0, false));
        }

        var direction = FeatureHelpers.RandomHorizontal(random);
        var horizontalLog = WithAxis(AxisOf(direction));
        var firstContinues = firstBranchStart < trunkHeight - 1;
        var first = this.Branch(
            tree, freeTreeHeight, origin, horizontalLog, direction, firstBranchStart, firstContinues);
        attachments.Add(first);

        if (hasSecondBranch)
        {
            var secondContinues = secondBranchStart < trunkHeight - 1;
            var opposite = direction.Opposite();
            var second = this.Branch(
                tree, freeTreeHeight, origin, horizontalLog, opposite, secondBranchStart, secondContinues);
            attachments.Add(second);
        }

        return attachments;
    }

    private static int TrunkHeight(int freeTreeHeight, int firstBranchStart, int secondBranchStart, int branches)
    {
        if (branches == 3)
        {
            return freeTreeHeight;
        }

        if (branches >= 2)
        {
            return Math.Max(firstBranchStart, secondBranchStart) + 1;
        }

        return firstBranchStart + 1;
    }

    private FoliageAttachment Branch(
        TreePlacement tree,
        int freeTreeHeight,
        Vector origin,
        Func<IBlock, IBlock> horizontalLog,
        BlockFace direction,
        int branchStart,
        bool middleContinuesAbove)
    {
        var random = tree.Random;
        var cursor = origin + (0, branchStart, 0);
        var endY = freeTreeHeight - 1 + this.branchEndOffset(random);
        var extendedStart = middleContinuesAbove || endY < branchStart;
        var horizontalLength = this.branchHorizontalLength(random) + (extendedStart ? 1 : 0);
        var step = Step(direction);
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
            var distance = DistManhattan(cursor, end);

            if (distance == 0)
            {
                return new FoliageAttachment(end + Vector.Up, 0, false);
            }

            var verticalChance = (float)Math.Abs(end.Y - cursor.Y) / distance;
            var moveVertically = random.NextFloat() < verticalChance;

            cursor += moveVertically ? vertical : step;
            this.PlaceLog(tree, cursor, moveVertically ? null : horizontalLog);
        }
    }
}

/// <summary>Mangrove: a trunk with branches that climb diagonally away from it.</summary>
internal sealed class UpwardsBranchingTrunkPlacer(FeatureRegistry registry, JsonElement json) : TrunkPlacer(json)
{
    private readonly IntProvider extraBranchSteps = ValueProviders.Int(json.GetProperty("extra_branch_steps"));
    private readonly IntProvider extraBranchLength = ValueProviders.Int(json.GetProperty("extra_branch_length"));
    private readonly HashSet<int> canGrowThrough = registry.Blocks(json.GetProperty("can_grow_through"));

    private readonly float placeBranchPerLogProbability =
        json.GetProperty("place_branch_per_log_probability").GetSingle();

    public override List<FoliageAttachment> PlaceTrunk(TreePlacement tree, int freeTreeHeight, Vector origin)
    {
        var random = tree.Random;
        var attachments = new List<FoliageAttachment>();

        for (var i = 0; i < freeTreeHeight; i++)
        {
            var y = origin.Y + i;
            var position = new Vector(origin.X, y, origin.Z);
            var placed = this.PlaceLog(tree, position);
            var belowTop = i < freeTreeHeight - 1;

            if (placed && belowTop && random.NextFloat() < this.placeBranchPerLogProbability)
            {
                var direction = FeatureHelpers.RandomHorizontal(random);
                var length = this.extraBranchLength(random);
                var branchStart = Math.Max(0, length - this.extraBranchLength(random) - 1);
                var steps = this.extraBranchSteps(random);

                this.PlaceBranch(tree, freeTreeHeight, attachments, position, y, direction, branchStart, steps);
            }

            if (i == freeTreeHeight - 1)
            {
                attachments.Add(new FoliageAttachment(new Vector(origin.X, y + 1, origin.Z), 0, false));
            }
        }

        return attachments;
    }

    protected override bool ValidTreePos(TreeTags tags, IWorldGenLevel level, Vector position) =>
        base.ValidTreePos(tags, level, position) || this.canGrowThrough.Contains(level.GetBlock(position).RegistryId);

    private void PlaceBranch(
        TreePlacement tree,
        int freeTreeHeight,
        List<FoliageAttachment> attachments,
        Vector trunkPosition,
        int trunkY,
        BlockFace direction,
        int branchStart,
        int steps)
    {
        var topY = trunkY + branchStart;
        var x = trunkPosition.X;
        var z = trunkPosition.Z;
        var step = Step(direction);

        for (var i = branchStart; i < freeTreeHeight && steps > 0; i++, steps--)
        {
            if (i < 1)
            {
                continue;
            }

            var y = trunkY + i;
            x += step.X;
            z += step.Z;
            topY = y;

            var position = new Vector(x, y, z);

            if (this.PlaceLog(tree, position))
            {
                topY++;
            }

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
