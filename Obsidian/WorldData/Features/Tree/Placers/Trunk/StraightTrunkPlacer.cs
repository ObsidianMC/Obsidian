namespace Obsidian.WorldData.Features.Tree.Placers.Trunk;

/// <summary>
/// A single straight column of logs (oak, birch, spruce...).
/// </summary>
[ConfiguredFeatureProperty("minecraft:straight_trunk_placer")]
public sealed class StraightTrunkPlacer : TrunkPlacer
{
    public override List<FoliageAttachment> PlaceTrunk(TreeContext tree, int freeTreeHeight, Vector origin)
    {
        SetDirtAt(tree, origin + Vector.Down);
        for (var y = 0; y < freeTreeHeight; y++)
            this.PlaceLog(tree, origin + (0, y, 0));

        return [new FoliageAttachment(origin + (0, freeTreeHeight, 0), 0, false)];
    }
}
