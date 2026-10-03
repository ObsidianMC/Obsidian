using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

/// <summary>
/// Scattered foliage (azalea, mangrove): tries <see cref="LeafPlacementAttempts"/> random spots around the attachment.
/// </summary>
[ConfiguredFeatureProperty("minecraft:random_spread_foliage_placer")]
public sealed class RandomSpreadFoliagePlacer : FoliagePlacer
{
    public required IIntProvider FoliageHeight { get; init; }

    public required int LeafPlacementAttempts { get; init; }

    public override int GetFoliageHeight(IRandomSource random, int treeHeight) => this.FoliageHeight.Sample(random);

    protected override void CreateFoliage(TreeContext tree, int freeTreeHeight, FoliageAttachment attachment, int foliageHeight,
        int foliageRadius, int offset)
    {
        var random = tree.Random;
        for (var i = 0; i < this.LeafPlacementAttempts; i++)
        {
            // Triangular offsets; arguments are evaluated left to right, matching vanilla's call order.
            var dx = random.NextInt(foliageRadius) - random.NextInt(foliageRadius);
            var dy = random.NextInt(foliageHeight) - random.NextInt(foliageHeight);
            var dz = random.NextInt(foliageRadius) - random.NextInt(foliageRadius);
            TryPlaceLeaf(tree, attachment.Position + (dx, dy, dz));
        }
    }

    protected override bool ShouldSkipLocation(IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk) => false;
}
