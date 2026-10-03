using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

/// <summary>
/// Spruce foliage: rows whose radius cycles up and back down, growing toward the bottom.
/// </summary>
[ConfiguredFeatureProperty("minecraft:spruce_foliage_placer")]
public sealed class SpruceFoliagePlacer : FoliagePlacer
{
    /// <summary>Bare trunk below the foliage; the foliage height is the tree height minus a sample (at least 4).</summary>
    public required IIntProvider TrunkHeight { get; init; }

    public override int GetFoliageHeight(IRandomSource random, int treeHeight) => Math.Max(4, treeHeight - this.TrunkHeight.Sample(random));

    protected override void CreateFoliage(TreeContext tree, int freeTreeHeight, FoliageAttachment attachment, int foliageHeight,
        int foliageRadius, int offset)
    {
        var radius = tree.Random.NextInt(2);
        var maxRadius = 1;
        var resetRadius = 0;
        for (var y = offset; y >= -foliageHeight; y--)
        {
            this.PlaceLeavesRow(tree, attachment.Position, radius, y, attachment.DoubleTrunk);
            if (radius >= maxRadius)
            {
                radius = resetRadius;
                resetRadius = 1;
                maxRadius = Math.Min(maxRadius + 1, foliageRadius + attachment.RadiusOffset);
            }
            else
            {
                radius++;
            }
        }
    }

    protected override bool ShouldSkipLocation(IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk) =>
        dx == radius && dz == radius && radius > 0;
}
