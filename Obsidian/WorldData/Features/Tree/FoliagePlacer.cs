using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.Tree;

/// <summary>
/// Places a tree's leaves around each <see cref="FoliageAttachment"/>, like vanilla's <c>FoliagePlacer</c>.
/// </summary>
public abstract class FoliagePlacer
{
    public string Type { get; init; } = string.Empty;

    public required IIntProvider Radius { get; init; }

    public required IIntProvider Offset { get; init; }

    /// <summary>Places foliage at one attachment; samples <see cref="Offset"/> once per call, like vanilla.</summary>
    public void CreateFoliage(TreeContext tree, int freeTreeHeight, FoliageAttachment attachment, int foliageHeight, int foliageRadius) =>
        this.CreateFoliage(tree, freeTreeHeight, attachment, foliageHeight, foliageRadius, this.Offset.Sample(tree.Random));

    /// <summary>Height of the foliage, used to compute the trunk length before the foliage radius is sampled.</summary>
    public abstract int GetFoliageHeight(IRandomSource random, int treeHeight);

    public virtual int GetFoliageRadius(IRandomSource random, int trunkHeight) => this.Radius.Sample(random);

    protected abstract void CreateFoliage(TreeContext tree, int freeTreeHeight, FoliageAttachment attachment, int foliageHeight,
        int foliageRadius, int offset);

    /// <summary>
    /// Whether to leave out the leaf at (<paramref name="dx"/>, <paramref name="y"/>, <paramref name="dz"/>) of a row of
    /// <paramref name="radius"/>; <paramref name="dx"/>/<paramref name="dz"/> are absolute distances from the trunk.
    /// </summary>
    protected abstract bool ShouldSkipLocation(IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk);

    /// <summary>Converts signed offsets to distances (measured from the nearer trunk column of a 2x2 trunk).</summary>
    protected virtual bool ShouldSkipLocationSigned(IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk)
    {
        int distanceX, distanceZ;
        if (doubleTrunk)
        {
            distanceX = Math.Min(Math.Abs(dx), Math.Abs(dx - 1));
            distanceZ = Math.Min(Math.Abs(dz), Math.Abs(dz - 1));
        }
        else
        {
            distanceX = Math.Abs(dx);
            distanceZ = Math.Abs(dz);
        }

        return this.ShouldSkipLocation(random, distanceX, y, distanceZ, radius, doubleTrunk);
    }

    /// <summary>Places a square row of leaves of <paramref name="radius"/> at <paramref name="y"/> blocks above <paramref name="center"/>.</summary>
    protected void PlaceLeavesRow(TreeContext tree, Vector center, int radius, int y, bool doubleTrunk)
    {
        var extra = doubleTrunk ? 1 : 0;
        for (var dx = -radius; dx <= radius + extra; dx++)
        {
            for (var dz = -radius; dz <= radius + extra; dz++)
            {
                if (!this.ShouldSkipLocationSigned(tree.Random, dx, y, dz, radius, doubleTrunk))
                    TryPlaceLeaf(tree, center + (dx, y, dz));
            }
        }
    }

    /// <summary>
    /// Places a row like <see cref="PlaceLeavesRow"/>, then randomly hangs one or two leaves below its outer edge
    /// (used by cherry trees).
    /// </summary>
    protected void PlaceLeavesRowWithHangingLeavesBelow(TreeContext tree, Vector center, int radius, int y, bool doubleTrunk,
        float hangingLeavesChance, float hangingLeavesExtensionChance)
    {
        this.PlaceLeavesRow(tree, center, radius, y, doubleTrunk);

        var extra = doubleTrunk ? 1 : 0;
        var below = center + Vector.Down;
        foreach (var direction in TreeDirections.Horizontal)
        {
            var clockWise = direction.ClockWise();
            var sideOffset = clockWise.IsPositive() ? radius + extra : radius;
            var step = direction.ToVector();
            var cursor = center + (0, y - 1, 0) + clockWise.ToVector() * sideOffset + step * -radius;

            for (var i = -radius; i < radius + extra; i++)
            {
                var hasLeafAbove = tree.IsFoliageSet(cursor + Vector.Up);
                if (hasLeafAbove && TryPlaceExtension(tree, hangingLeavesChance, below, cursor))
                    TryPlaceExtension(tree, hangingLeavesExtensionChance, below, cursor + Vector.Down);

                cursor += step;
            }
        }
    }

    /// <summary>
    /// Places a leaf unless the position holds persistent leaves or isn't <see cref="TreeBlocks.ValidTreePos"/>;
    /// leaves that can be waterlogged are waterlogged in water source blocks.
    /// </summary>
    protected static bool TryPlaceLeaf(TreeContext tree, Vector position)
    {
        var existing = tree.Level.GetBlock(position);
        var persistent = existing.GetProperty("persistent") == "true";
        if (persistent || !TreeBlocks.ValidTreePos(tree.Level, position))
            return false;

        var leaf = tree.Config.FoliageProvider.GetState(tree.Random, position);
        if (leaf.HasProperty("waterlogged"))
            leaf = leaf.WithProperty("waterlogged", TreeBlocks.IsWaterSource(tree.Level.GetBlock(position)));

        tree.SetFoliage(position, leaf);
        return true;
    }

    private static bool TryPlaceExtension(TreeContext tree, float chance, Vector origin, Vector position)
    {
        if (TreeDirections.DistManhattan(position, origin) >= 7)
            return false;

        return !(tree.Random.NextFloat() > chance) && TryPlaceLeaf(tree, position);
    }
}
