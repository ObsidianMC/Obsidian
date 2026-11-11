using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.Plugins;

namespace Obsidian.WorldData;

/// <summary>Reads the last published shapes without creating block entities or invoking plugin code.</summary>
internal static class BlockEntityShapes
{
    public static BlockShapes? Get(ILevel level, Vector position, IBlock block) =>
        Current(level, position, block)?.ShapeData;

    /// <summary>
    /// The collision boxes the block entity gives its block, relative to it, or null for the state's own: what its
    /// shapes say, or for a shulker box whose lid is open or moving, the box the lid fills, which reaches out of the
    /// block.
    /// </summary>
    public static IReadOnlyList<BoundingBox>? GetCollision(ILevel level, Vector position, IBlock block)
    {
        var current = Current(level, position, block);
        if (current is ShulkerBoxBlockEntity shulker)
            return ShulkerLids.CollisionBox(block, shulker.LidProgress) is BoundingBox lid ? [lid] : null;

        return current?.ShapeData?.Collision;
    }

    private static BlockEntity? Current(ILevel level, Vector position, IBlock block)
    {
        if (!block.HasBlockEntity() || level is not AbstractLevel concrete)
            return null;

        var entity = concrete.GetLoadedChunk(position.X >> 4, position.Z >> 4)
            ?.GetBlockEntity(position.X, position.Y, position.Z);

        if (entity is not BlockEntity { IsRemoved: false } current
            || current.Owner is PluginContainer { IsClosing: true }
            || current.Id != block.BlockEntityType())
        {
            return null;
        }

        return current;
    }
}
