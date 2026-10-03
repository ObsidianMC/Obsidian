using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

/// <summary>
/// Fancy oak clusters: round rows, one block wider in the middle layers.
/// </summary>
[ConfiguredFeatureProperty("minecraft:fancy_foliage_placer")]
public sealed class FancyFoliagePlacer : BlobFoliagePlacer
{
    protected override void CreateFoliage(TreeContext tree, int freeTreeHeight, FoliageAttachment attachment, int foliageHeight,
        int foliageRadius, int offset)
    {
        for (var y = offset; y >= offset - foliageHeight; y--)
        {
            var radius = foliageRadius + (y != offset && y != offset - foliageHeight ? 1 : 0);
            this.PlaceLeavesRow(tree, attachment.Position, radius, y, attachment.DoubleTrunk);
        }
    }

    // Circle test in float, like vanilla's Mth.square(x + 0.5F).
    protected override bool ShouldSkipLocation(IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk) =>
        Square(dx + 0.5f) + Square(dz + 0.5f) > radius * radius;

    private static float Square(float value) => value * value;
}
