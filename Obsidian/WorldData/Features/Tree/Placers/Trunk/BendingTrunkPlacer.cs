namespace Obsidian.WorldData.Features.Tree.Placers.Trunk;

/// <summary>
/// Azalea trunk: grows up, bends sideways near the top and continues horizontally for <see cref="BendLength"/> blocks.
/// </summary>
[ConfiguredFeatureProperty("minecraft:bending_trunk_placer")]
public sealed class BendingTrunkPlacer : TrunkPlacer
{
    /// <summary>Trunk blocks from this height up also get foliage.</summary>
    public int MinHeightForLeaves { get; init; } = 1;

    public required IIntProvider BendLength { get; init; }

    public override List<FoliageAttachment> PlaceTrunk(TreeContext tree, int freeTreeHeight, Vector origin)
    {
        var random = tree.Random;
        var direction = TreeDirections.RandomHorizontal(random);
        var step = direction.ToVector();
        var top = freeTreeHeight - 1;
        var cursor = origin;
        SetDirtAt(tree, cursor + Vector.Down);

        var attachments = new List<FoliageAttachment>();
        for (var i = 0; i <= top; i++)
        {
            if (i + 1 >= top + random.NextInt(2))
                cursor += step;

            if (TreeBlocks.ValidTreePos(tree.Level, cursor))
                this.PlaceLog(tree, cursor);

            if (i >= this.MinHeightForLeaves)
                attachments.Add(new FoliageAttachment(cursor, 0, false));

            cursor += Vector.Up;
        }

        var bendLength = this.BendLength.Sample(random);
        for (var i = 0; i <= bendLength; i++)
        {
            if (TreeBlocks.ValidTreePos(tree.Level, cursor))
                this.PlaceLog(tree, cursor);

            attachments.Add(new FoliageAttachment(cursor, 0, false));
            cursor += step;
        }

        return attachments;
    }
}
