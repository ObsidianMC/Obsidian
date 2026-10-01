namespace Obsidian.WorldData.Features.Tree;

/// <summary>
/// Port of vanilla <c>StructureTemplate.updateShapeAtEdge</c> as run after tree placement: every block on the tree's
/// surface and the block facing it get a neighbor shape update.
/// </summary>
/// <remarks>
/// Obsidian has no general <c>updateShape</c> implementation, so only the block behaviors that matter around generated
/// trees are reproduced: vines re-checking their supports, double plants losing their other half, snowy dirt
/// (<c>snowy</c> from the block above) and hanging moss (<c>tip</c>). Other blocks keep their state, which matches vanilla
/// for logs, leaves (they only schedule ticks), fluids, roots and most plants.
/// </remarks>
internal static class ShapeUpdater
{
    private static readonly IBlock air = BlockStateProperties.GetState("minecraft:air");

    private static readonly BlockSet farmland = new("minecraft:farmland");

    public static void UpdateShapeAtEdge(IWorldGenLevel level, TreeVoxelShape shape, Vector origin)
    {
        shape.ForAllFaces((face, x, y, z) =>
        {
            var position = origin + (x, y, z);
            var neighborPosition = position + face.ToVector();
            var state = level.GetBlock(position);
            var neighbor = level.GetBlock(neighborPosition);

            var updated = UpdateShape(level, state, position, face, neighbor);
            if (!updated.IsSameState(state))
                level.SetBlock(position, updated);

            var updatedNeighbor = UpdateShape(level, neighbor, neighborPosition, face.Opposite(), updated);
            if (!updatedNeighbor.IsSameState(neighbor))
                level.SetBlock(neighborPosition, updatedNeighbor);
        });
    }

    /// <summary>The state of <paramref name="state"/> after its neighbor in <paramref name="direction"/> became <paramref name="neighbor"/>.</summary>
    private static IBlock UpdateShape(IWorldGenLevel level, IBlock state, Vector position, BlockFace direction, IBlock neighbor)
    {
        switch (state.BlockClass())
        {
            case "VineBlock":
                return direction == BlockFace.Down ? state : UpdateVine(level, state, position);
            case "DoublePlantBlock" or "TallFlowerBlock":
                return UpdateDoublePlant(level, state, position, direction, neighbor);
            case "HangingMossBlock":
                return state.WithProperty("tip", level.GetBlock(position + Vector.Down).RegistryId != state.RegistryId);
        }

        // SnowyDirtBlock and its subclasses (grass blocks, mycelium, podzol).
        if (direction == BlockFace.Up && state.HasProperty("snowy"))
            return state.WithProperty("snowy", TreeBlocks.Snow.Contains(neighbor));

        return state;
    }

    /// <summary>Vanilla <c>VineBlock.getUpdatedState</c>; a vine left without faces becomes air.</summary>
    private static IBlock UpdateVine(IWorldGenLevel level, IBlock state, Vector position)
    {
        var abovePosition = position + Vector.Up;
        if (state.GetProperty("up") == "true")
            state = state.WithProperty("up", CanAttachTo(level.GetBlock(abovePosition), BlockFace.Down));

        IBlock? above = null;
        foreach (var face in TreeDirections.Horizontal)
        {
            var property = face.PropertyName();
            if (state.GetProperty(property) != "true")
                continue;

            var supported = CanAttachTo(level.GetBlock(position + face.ToVector()), face);
            if (!supported)
            {
                // Hanging from a vine above with the same face.
                above ??= level.GetBlock(abovePosition);
                supported = above.RegistryId == state.RegistryId && above.GetProperty(property) == "true";
            }

            state = state.WithProperty(property, supported);
        }

        var hasFace = state.GetProperty("up") == "true" || TreeDirections.Horizontal.Any(face => state.GetProperty(face.PropertyName()) == "true");
        return hasFace ? state : air;
    }

    /// <summary>
    /// Vanilla <c>MultifaceBlock.canAttachTo</c>: the neighbor's support or collision shape covers the face touching the
    /// vine. Approximated with the sturdy-face flag and full collision cubes from the block physics data.
    /// </summary>
    private static bool CanAttachTo(IBlock neighbor, BlockFace direction) =>
        neighbor.IsFaceSturdy(direction.Opposite()) || neighbor.IsCollisionShapeFullBlock();

    /// <summary>Vanilla <c>DoublePlantBlock.updateShape</c>: a half whose partner disappeared turns into air.</summary>
    private static IBlock UpdateDoublePlant(IWorldGenLevel level, IBlock state, Vector position, BlockFace direction, IBlock neighbor)
    {
        var isLower = state.GetProperty("half") == "lower";
        var vertical = direction is BlockFace.Up or BlockFace.Down;
        var partnerStillThere = neighbor.RegistryId == state.RegistryId && neighbor.GetProperty("half") != state.GetProperty("half");

        if (!vertical || isLower != (direction == BlockFace.Up) || partnerStillThere)
        {
            if (isLower && direction == BlockFace.Down)
            {
                // VegetationBlock.mayPlaceOn: dirt or farmland below.
                var below = level.GetBlock(position + Vector.Down);
                if (!TreeBlocks.Dirt.Contains(below) && !farmland.Contains(below))
                    return air;
            }

            return state;
        }

        return air;
    }
}
