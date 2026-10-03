using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

/// <summary>
/// The classic oak/birch blob: rows shrinking toward the top, with randomly trimmed corners.
/// </summary>
[ConfiguredFeatureProperty("minecraft:blob_foliage_placer")]
public class BlobFoliagePlacer : FoliagePlacer
{
    public required int Height { get; init; }

    public override int GetFoliageHeight(IRandomSource random, int treeHeight) => this.Height;

    protected override void CreateFoliage(TreeContext tree, int freeTreeHeight, FoliageAttachment attachment, int foliageHeight,
        int foliageRadius, int offset)
    {
        for (var y = offset; y >= offset - foliageHeight; y--)
        {
            var radius = Math.Max(foliageRadius + attachment.RadiusOffset - 1 - y / 2, 0);
            this.PlaceLeavesRow(tree, attachment.Position, radius, y, attachment.DoubleTrunk);
        }
    }

    protected override bool ShouldSkipLocation(IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk) =>
        dx == radius && dz == radius && (random.NextInt(2) == 0 || y == 0);
}
