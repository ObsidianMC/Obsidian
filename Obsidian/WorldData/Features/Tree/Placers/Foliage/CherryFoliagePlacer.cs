using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

/// <summary>
/// Cherry canopy: wide layered rows with random holes and leaves hanging from the two bottom rows.
/// </summary>
[ConfiguredFeatureProperty("minecraft:cherry_foliage_placer")]
public sealed class CherryFoliagePlacer : FoliagePlacer
{
    public required IIntProvider Height { get; init; }

    public required float WideBottomLayerHoleChance { get; init; }

    public required float CornerHoleChance { get; init; }

    public required float HangingLeavesChance { get; init; }

    public required float HangingLeavesExtensionChance { get; init; }

    public override int GetFoliageHeight(IRandomSource random, int treeHeight) => this.Height.Sample(random);

    protected override void CreateFoliage(TreeContext tree, int freeTreeHeight, FoliageAttachment attachment, int foliageHeight,
        int foliageRadius, int offset)
    {
        var doubleTrunk = attachment.DoubleTrunk;
        var center = attachment.Position + (0, offset, 0);
        var radius = foliageRadius + attachment.RadiusOffset - 1;

        this.PlaceLeavesRow(tree, center, radius - 2, foliageHeight - 3, doubleTrunk);
        this.PlaceLeavesRow(tree, center, radius - 1, foliageHeight - 4, doubleTrunk);
        for (var y = foliageHeight - 5; y >= 0; y--)
            this.PlaceLeavesRow(tree, center, radius, y, doubleTrunk);

        this.PlaceLeavesRowWithHangingLeavesBelow(tree, center, radius, -1, doubleTrunk, this.HangingLeavesChance, this.HangingLeavesExtensionChance);
        this.PlaceLeavesRowWithHangingLeavesBelow(tree, center, radius - 1, -2, doubleTrunk, this.HangingLeavesChance, this.HangingLeavesExtensionChance);
    }

    protected override bool ShouldSkipLocation(IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk)
    {
        if (y == -1 && (dx == radius || dz == radius) && random.NextFloat() < this.WideBottomLayerHoleChance)
            return true;

        var isCorner = dx == radius && dz == radius;
        if (radius > 2)
            return isCorner || (dx + dz > radius * 2 - 2 && random.NextFloat() < this.CornerHoleChance);

        return isCorner && random.NextFloat() < this.CornerHoleChance;
    }
}
