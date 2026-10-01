using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

/// <summary>
/// Jungle bush foliage: rows shrinking by one per layer, corners randomly trimmed.
/// </summary>
[ConfiguredFeatureProperty("minecraft:bush_foliage_placer")]
public sealed class BushFoliagePlacer : BlobFoliagePlacer
{
    protected override void CreateFoliage(TreeContext tree, int freeTreeHeight, FoliageAttachment attachment, int foliageHeight,
        int foliageRadius, int offset)
    {
        for (var y = offset; y >= offset - foliageHeight; y--)
        {
            var radius = foliageRadius + attachment.RadiusOffset - 1 - y;
            this.PlaceLeavesRow(tree, attachment.Position, radius, y, attachment.DoubleTrunk);
        }
    }

    protected override bool ShouldSkipLocation(IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk) =>
        dx == radius && dz == radius && random.NextInt(2) == 0;
}
