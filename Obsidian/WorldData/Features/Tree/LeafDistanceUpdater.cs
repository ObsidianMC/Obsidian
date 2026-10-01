namespace Obsidian.WorldData.Features.Tree;

/// <summary>
/// Port of vanilla <c>TreeFeature.updateLeaves</c>: sets the <c>distance</c> of the tree's leaves by a breadth-first walk
/// out from its logs, and returns the cells that make up the tree for <see cref="ShapeUpdater"/>.
/// </summary>
/// <remarks>
/// Faithful to a vanilla quirk: a leaf can be queued at a higher distance by a same-distance neighbor processed before it,
/// and is then overwritten with that higher distance later. Which leaves are affected depends on Java hash set iteration
/// order, hence <see cref="VanillaBlockPosSet"/>.
/// </remarks>
internal static class LeafDistanceUpdater
{
    private const int MaxDistance = 7;

    public static TreeVoxelShape UpdateLeaves(IWorldGenLevel level, TreeBounds bounds, VanillaBlockPosSet logs,
        VanillaBlockPosSet decorations, VanillaBlockPosSet roots)
    {
        var shape = new TreeVoxelShape(bounds.SizeX, bounds.SizeY, bounds.SizeZ);
        var queues = new VanillaBlockPosSet[MaxDistance];
        for (var i = 0; i < MaxDistance; i++)
            queues[i] = new VanillaBlockPosSet();

        // Decorations and roots belong to the tree's shape but aren't walked through.
        FillAll(shape, bounds, decorations);
        FillAll(shape, bounds, roots);

        queues[0].AddAll(logs);
        var distance = 0;
        while (true)
        {
            while (distance >= MaxDistance || queues[distance].Count != 0)
            {
                if (distance >= MaxDistance)
                    return shape;

                var position = queues[distance].RemoveFirst();
                if (!bounds.IsInside(position))
                    continue;

                if (distance != 0)
                    level.SetBlock(position, level.GetBlock(position).WithProperty("distance", distance));

                Fill(shape, bounds, position);
                foreach (var face in TreeDirections.All)
                {
                    var neighbor = position + face.ToVector();
                    if (!bounds.IsInside(neighbor))
                        continue;

                    var local = neighbor - bounds.Min;
                    if (shape.IsFull(local.X, local.Y, local.Z))
                        continue;

                    var neighborDistance = TreeBlocks.OptionalLeafDistance(level.GetBlock(neighbor));
                    if (neighborDistance is null)
                        continue;

                    var queued = Math.Min(neighborDistance.Value, distance + 1);
                    if (queued < MaxDistance)
                    {
                        queues[queued].Add(neighbor);
                        distance = Math.Min(distance, queued);
                    }
                }
            }

            distance++;
        }
    }

    private static void FillAll(TreeVoxelShape shape, TreeBounds bounds, VanillaBlockPosSet positions)
    {
        foreach (var position in positions)
        {
            if (bounds.IsInside(position))
                Fill(shape, bounds, position);
        }
    }

    private static void Fill(TreeVoxelShape shape, TreeBounds bounds, Vector position)
    {
        var local = position - bounds.Min;
        shape.Fill(local.X, local.Y, local.Z);
    }
}
