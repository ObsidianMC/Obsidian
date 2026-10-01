using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

/// <summary>
/// Pine foliage: a narrow cone, widening downward and pulling in again at the bottom row.
/// </summary>
[ConfiguredFeatureProperty("minecraft:pine_foliage_placer")]
public sealed class PineFoliagePlacer : FoliagePlacer
{
    public required IIntProvider Height { get; init; }

    public override int GetFoliageHeight(IRandomSource random, int treeHeight) => this.Height.Sample(random);

    /// <summary>Adds a random extra radius of up to the trunk height.</summary>
    public override int GetFoliageRadius(IRandomSource random, int trunkHeight) =>
        base.GetFoliageRadius(random, trunkHeight) + random.NextInt(Math.Max(trunkHeight + 1, 1));

    protected override void CreateFoliage(TreeContext tree, int freeTreeHeight, FoliageAttachment attachment, int foliageHeight,
        int foliageRadius, int offset)
    {
        var radius = 0;
        for (var y = offset; y >= offset - foliageHeight; y--)
        {
            this.PlaceLeavesRow(tree, attachment.Position, radius, y, attachment.DoubleTrunk);
            if (radius >= 1 && y == offset - foliageHeight + 1)
                radius--;
            else if (radius < foliageRadius + attachment.RadiusOffset)
                radius++;
        }
    }

    protected override bool ShouldSkipLocation(IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk) =>
        dx == radius && dz == radius && radius > 0;
}
