using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

/// <summary>
/// Jungle foliage (registered as <c>jungle_foliage_placer</c>): round rows widening downward; branches get one or two rows.
/// </summary>
[ConfiguredFeatureProperty("minecraft:jungle_foliage_placer")]
public sealed class MegaJungleFoliagePlacer : FoliagePlacer
{
    public required int Height { get; init; }

    public override int GetFoliageHeight(IRandomSource random, int treeHeight) => this.Height;

    protected override void CreateFoliage(TreeContext tree, int freeTreeHeight, FoliageAttachment attachment, int foliageHeight,
        int foliageRadius, int offset)
    {
        var rows = attachment.DoubleTrunk ? foliageHeight : 1 + tree.Random.NextInt(2);
        for (var y = offset; y >= offset - rows; y--)
        {
            var radius = foliageRadius + attachment.RadiusOffset + 1 - y;
            this.PlaceLeavesRow(tree, attachment.Position, radius, y, attachment.DoubleTrunk);
        }
    }

    protected override bool ShouldSkipLocation(IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk) =>
        dx + dz >= 7 || dx * dx + dz * dz > radius * radius;
}
