using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

/// <summary>
/// Acacia canopy: a wide flat layer with a smaller layer on top.
/// </summary>
[ConfiguredFeatureProperty("minecraft:acacia_foliage_placer")]
public sealed class AcaciaFoliagePlacer : FoliagePlacer
{
    public override int GetFoliageHeight(IRandomSource random, int treeHeight) => 0;

    protected override void CreateFoliage(TreeContext tree, int freeTreeHeight, FoliageAttachment attachment, int foliageHeight,
        int foliageRadius, int offset)
    {
        var doubleTrunk = attachment.DoubleTrunk;
        var center = attachment.Position + (0, offset, 0);
        this.PlaceLeavesRow(tree, center, foliageRadius + attachment.RadiusOffset, -1 - foliageHeight, doubleTrunk);
        this.PlaceLeavesRow(tree, center, foliageRadius - 1, -foliageHeight, doubleTrunk);
        this.PlaceLeavesRow(tree, center, foliageRadius + attachment.RadiusOffset - 1, 0, doubleTrunk);
    }

    protected override bool ShouldSkipLocation(IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk) =>
        y == 0
            ? (dx > 1 || dz > 1) && dx != 0 && dz != 0
            : dx == radius && dz == radius && radius > 0;
}
