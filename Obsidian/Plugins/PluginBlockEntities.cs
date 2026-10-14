using Obsidian.API.BlockEntities;
using Obsidian.Nbt;
using Obsidian.Plugins.PluginProviders;
using Obsidian.WorldData;

namespace Obsidian.Plugins;

/// <summary>
/// The block entities plugins own: those of their block entity types, and those of their classes. Their classes come
/// from the plugin's load context, so unloading the plugin replaces them with their saved data.
/// </summary>
internal static class PluginBlockEntities
{
    /// <summary>
    /// Replaces the block entities <paramref name="plugin"/> owns in a level's loaded chunks with what they saved, as
    /// it unloads, like saved block entities of types the server doesn't know: they're saved again as they were, and
    /// the level creates them again from it once the plugin is loaded again. Unloaded chunks keep theirs as saved
    /// the same way when they load.
    /// </summary>
    /// <remarks>
    /// Run between the level's ticks, so that none is mid-tick. A block entity whose change the level didn't take in
    /// yet saves once more, in a call the unloading makes; then nothing of the plugin's runs. Removing them has no side
    /// effects: the blocks stay.
    /// </remarks>
    public static void RemoveFrom(AbstractLevel level, PluginContainer plugin)
    {
        foreach (var chunk in level.Regions.Values.SelectMany(region => region.GeneratedChunks()).OfType<Chunk>())
        {
            foreach (var blockEntity in chunk.BlockEntities.Values.OfType<BlockEntity>())
            {
                if (blockEntity is DataBlockEntity || !IsOwnedBy(blockEntity, plugin))
                    continue;

                var kept = new DataBlockEntity
                {
                    Id = blockEntity.Id,
                    BlockPosition = blockEntity.BlockPosition
                };

                kept.Load(SavedData(blockEntity, plugin) ?? new NbtCompound());
                level.AttachBlockEntity(kept);

                if (!chunk.ReplaceBlockEntity(blockEntity, kept))
                    continue;

                blockEntity.IsRemoved = true;
                blockEntity.Host = null;
                blockEntity.Owner = null;
            }
        }
    }

    private static bool IsOwnedBy(BlockEntity blockEntity, PluginContainer plugin) =>
        blockEntity.Owner == plugin || PluginLoadContext.OwnerOf(blockEntity.GetType().Assembly) == plugin;

    // The block entity's data as of its last change: one the level didn't take in yet is saved now.
    private static NbtCompound? SavedData(BlockEntity blockEntity, PluginContainer plugin)
    {
        var changed = blockEntity.TakeChange()
            || blockEntity is ContainerBlockEntity container && container.Inventory.Revision != container.SavedRevision;

        if (!changed)
            return blockEntity.SavedData;

        using var call = plugin.EnterUnloading();

        try
        {
            return BlockEntityNbt.SaveData(blockEntity);
        }
        catch (Exception)
        {
            // Its last good save stands.
            return blockEntity.SavedData;
        }
    }
}
