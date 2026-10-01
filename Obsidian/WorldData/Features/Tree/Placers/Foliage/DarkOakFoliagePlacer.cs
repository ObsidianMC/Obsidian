using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

/// <summary>
/// Dark oak canopy: a wide, flat crown on the 2x2 trunk and smaller tufts on the side stubs.
/// </summary>
[ConfiguredFeatureProperty("minecraft:dark_oak_foliage_placer")]
public sealed class DarkOakFoliagePlacer : FoliagePlacer
{
    public override int GetFoliageHeight(IRandomSource random, int treeHeight) => 4;

    protected override void CreateFoliage(TreeContext tree, int freeTreeHeight, FoliageAttachment attachment, int foliageHeight,
        int foliageRadius, int offset)
    {
        var center = attachment.Position + (0, offset, 0);
        var doubleTrunk = attachment.DoubleTrunk;
        if (doubleTrunk)
        {
            this.PlaceLeavesRow(tree, center, foliageRadius + 2, -1, doubleTrunk);
            this.PlaceLeavesRow(tree, center, foliageRadius + 3, 0, doubleTrunk);
            this.PlaceLeavesRow(tree, center, foliageRadius + 2, 1, doubleTrunk);
            if (tree.Random.NextBoolean())
                this.PlaceLeavesRow(tree, center, foliageRadius, 2, doubleTrunk);
        }
        else
        {
            this.PlaceLeavesRow(tree, center, foliageRadius + 2, -1, doubleTrunk);
            this.PlaceLeavesRow(tree, center, foliageRadius + 1, 0, doubleTrunk);
        }
    }

    /// <summary>The corners of the main crown's middle layer are always cut.</summary>
    protected override bool ShouldSkipLocationSigned(IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk)
    {
        if (y != 0 || !doubleTrunk || (dx != -radius && dx < radius) || (dz != -radius && dz < radius))
            return base.ShouldSkipLocationSigned(random, dx, y, dz, radius, doubleTrunk);

        return true;
    }

    protected override bool ShouldSkipLocation(IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk)
    {
        if (y == -1 && !doubleTrunk)
            return dx == radius && dz == radius;

        return y == 1 && dx + dz > radius * 2 - 2;
    }
}
