using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

/// <summary>
/// Mega spruce/pine crown: round rows widening toward the bottom of the crown.
/// </summary>
[ConfiguredFeatureProperty("minecraft:mega_pine_foliage_placer")]
public sealed class MegaPineFoliagePlacer : FoliagePlacer
{
    public required IIntProvider CrownHeight { get; init; }

    public override int GetFoliageHeight(IRandomSource random, int treeHeight) => this.CrownHeight.Sample(random);

    protected override void CreateFoliage(TreeContext tree, int freeTreeHeight, FoliageAttachment attachment, int foliageHeight,
        int foliageRadius, int offset)
    {
        var center = attachment.Position;
        var previousRadius = 0;
        for (var y = center.Y - foliageHeight + offset; y <= center.Y + offset; y++)
        {
            var depth = center.Y - y;
            var radius = foliageRadius + attachment.RadiusOffset + Mth.Floor((float)depth / foliageHeight * 3.5f);
            var rowRadius = depth > 0 && radius == previousRadius && (y & 1) == 0 ? radius + 1 : radius;

            this.PlaceLeavesRow(tree, new Vector(center.X, y, center.Z), rowRadius, 0, attachment.DoubleTrunk);
            previousRadius = radius;
        }
    }

    protected override bool ShouldSkipLocation(IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk) =>
        dx + dz >= 7 || dx * dx + dz * dz > radius * radius;
}
