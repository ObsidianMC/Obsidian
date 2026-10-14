using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.Plugins;

namespace Obsidian.WorldData;

/// <summary>Reads the last published shapes without creating block entities or invoking plugin code.</summary>
internal static class BlockEntityShapes
{
    public static BlockShapes? Get(ILevel level, Vector position, IBlock block)
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

        return current.ShapeData;
    }
}
