using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.Tree.Placers.Trunk;

/// <summary>
/// Fancy (big) oak trunk: a tall trunk with sloped branches toward foliage clusters spread around the crown.
/// </summary>
[ConfiguredFeatureProperty("minecraft:fancy_trunk_placer")]
public sealed class FancyTrunkPlacer : TrunkPlacer
{
    private const double TrunkHeightScale = 0.618;
    private const double ClusterDensityMagic = 1.382;
    private const double BranchSlope = 0.381;
    private const double BranchLengthMagic = 0.328;

    public override List<FoliageAttachment> PlaceTrunk(TreeContext tree, int freeTreeHeight, Vector origin)
    {
        var random = tree.Random;
        var height = freeTreeHeight + 2;
        var trunkHeight = Mth.Floor(height * TrunkHeightScale);
        SetDirtAt(tree, origin + Vector.Down);

        var clustersPerY = Math.Min(1, Mth.Floor(ClusterDensityMagic + Math.Pow(1.0 * height / 13.0, 2.0)));
        var trunkTop = origin.Y + trunkHeight;
        var relativeY = height - 5;
        var clusters = new List<(FoliageAttachment Attachment, int BranchBase)>
        {
            (new FoliageAttachment(origin + (0, relativeY, 0), 0, false), trunkTop)
        };

        for (; relativeY >= 0; relativeY--)
        {
            var shape = TreeShape(height, relativeY);
            if (shape < 0.0f)
                continue;

            for (var i = 0; i < clustersPerY; i++)
            {
                var radius = 1.0 * shape * (random.NextFloat() + BranchLengthMagic);
                var angle = random.NextFloat() * 2.0f * Math.PI;
                var offsetX = radius * Math.Sin(angle) + 0.5;
                var offsetZ = radius * Math.Cos(angle) + 0.5;
                var clusterBase = origin + (Mth.Floor(offsetX), relativeY - 1, Mth.Floor(offsetZ));
                var clusterTop = clusterBase + (0, 5, 0);
                if (!this.MakeLimb(tree, clusterBase, clusterTop, false))
                    continue;

                var dx = origin.X - clusterBase.X;
                var dz = origin.Z - clusterBase.Z;
                var branchY = clusterBase.Y - Math.Sqrt(dx * dx + dz * dz) * BranchSlope;
                var branchBaseY = branchY > trunkTop ? trunkTop : (int)branchY;
                var branchBase = new Vector(origin.X, branchBaseY, origin.Z);
                if (this.MakeLimb(tree, branchBase, clusterBase, false))
                    clusters.Add((new FoliageAttachment(clusterBase, 0, false), branchBase.Y));
            }
        }

        this.MakeLimb(tree, origin, origin + (0, trunkHeight, 0), true);
        this.MakeBranches(tree, height, origin, clusters);

        var attachments = new List<FoliageAttachment>();
        foreach (var (attachment, branchBase) in clusters)
        {
            if (TrimBranches(height, branchBase - origin.Y))
                attachments.Add(attachment);
        }

        return attachments;
    }

    /// <summary>
    /// Walks a straight line of blocks from <paramref name="start"/> to <paramref name="end"/>, placing logs (oriented
    /// along the dominant horizontal axis) or, when only checking, returning whether every block is free.
    /// </summary>
    private bool MakeLimb(TreeContext tree, Vector start, Vector end, bool place)
    {
        if (!place && start == end)
            return true;

        var delta = end - start;
        var steps = Math.Max(Math.Abs(delta.X), Math.Max(Math.Abs(delta.Y), Math.Abs(delta.Z)));
        var stepX = (float)delta.X / steps;
        var stepY = (float)delta.Y / steps;
        var stepZ = (float)delta.Z / steps;

        for (var i = 0; i <= steps; i++)
        {
            var position = start + (Mth.Floor(0.5f + i * stepX), Mth.Floor(0.5f + i * stepY), Mth.Floor(0.5f + i * stepZ));
            if (place)
            {
                this.PlaceLog(tree, position, WithAxis(GetLogAxis(start, position)));
            }
            else if (!this.IsFree(tree.Level, position))
            {
                return false;
            }
        }

        return true;
    }

    private static string GetLogAxis(Vector start, Vector position)
    {
        var dx = Math.Abs(position.X - start.X);
        var dz = Math.Abs(position.Z - start.Z);
        var max = Math.Max(dx, dz);
        if (max <= 0)
            return "y";

        return dx == max ? "x" : "z";
    }

    /// <summary>Clusters whose branch starts in the lowest 20% of the tree get no branch and no foliage.</summary>
    private static bool TrimBranches(int height, int branchBaseHeight) => branchBaseHeight >= height * 0.2;

    private void MakeBranches(TreeContext tree, int height, Vector origin, List<(FoliageAttachment Attachment, int BranchBase)> clusters)
    {
        foreach (var (attachment, branchBaseY) in clusters)
        {
            var branchBase = new Vector(origin.X, branchBaseY, origin.Z);
            if (branchBase != attachment.Position && TrimBranches(height, branchBaseY - origin.Y))
                this.MakeLimb(tree, branchBase, attachment.Position, true);
        }
    }

    /// <summary>Crown radius at <paramref name="y"/>, or negative below 30% of the height (no clusters there).</summary>
    private static float TreeShape(int height, int y)
    {
        if (y < height * 0.3f)
            return -1.0f;

        var half = height / 2.0f;
        var fromCenter = half - y;
        var radius = MathF.Sqrt(half * half - fromCenter * fromCenter);
        if (fromCenter == 0.0f)
            radius = half;
        else if (Math.Abs(fromCenter) >= half)
            return 0.0f;

        return radius * 0.5f;
    }
}
