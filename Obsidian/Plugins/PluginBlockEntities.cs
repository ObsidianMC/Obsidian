using Obsidian.API.BlockEntities;
using Obsidian.Entities;
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
    /// Replaces the block entities <paramref name="plugin"/> owns in a level's chunks, complete or still generating,
    /// with what they saved, as it unloads, like saved block entities of types the server doesn't know: they're saved
    /// again as they were, and the level creates them again from it once the plugin is loaded again. Unloaded chunks
    /// keep theirs as saved the same way when they load.
    /// </summary>
    /// <remarks>
    /// Run between the level's ticks, so that none is mid-tick. A block entity whose change the level didn't take in
    /// yet saves once more, in a call the unloading makes; then nothing of the plugin's runs. Removing them has no side
    /// effects: the blocks stay. Close the windows showing the containers returned with
    /// <see cref="CloseWindowsAsync"/>.
    /// </remarks>
    /// <returns>The inventories of the containers replaced, retired so that no click changes them anymore.</returns>
    public static List<BaseContainer> RemoveFrom(AbstractLevel level, PluginContainer plugin)
    {
        var retired = new List<BaseContainer>();

        foreach (var chunk in ResidentChunks(level))
        {
            foreach (var blockEntity in chunk.BlockEntities.Values.OfType<BlockEntity>())
            {
                if (blockEntity is DataBlockEntity || !IsOwnedBy(blockEntity, plugin))
                    continue;

                if (Keep(level, chunk, blockEntity, plugin) is BaseContainer inventory)
                    retired.Add(inventory);
            }
        }

        return retired;
    }

    /// <summary>
    /// Whether a level's chunks hold any of <paramref name="plugin"/>'s block entities. Once the plugin is closed,
    /// none is created anymore (see <see cref="BlockEntityNbt.Create"/>).
    /// </summary>
    public static bool AnyIn(AbstractLevel level, PluginContainer plugin) => ResidentChunks(level)
        .SelectMany(chunk => chunk.BlockEntities.Values.OfType<BlockEntity>())
        .Any(blockEntity => blockEntity is not DataBlockEntity && IsOwnedBy(blockEntity, plugin));

    /// <summary>
    /// Replaces one of <paramref name="plugin"/>'s block entities in <paramref name="chunk"/> with its saved data (see
    /// <see cref="RemoveFrom"/>).
    /// </summary>
    /// <returns>Its container's inventory, retired, if it's a container.</returns>
    internal static BaseContainer? Keep(
        AbstractLevel level,
        Chunk chunk,
        BlockEntity blockEntity,
        PluginContainer plugin)
    {
        lock (level.Fluids.TickLock)
            return KeepCore(level, chunk, blockEntity, plugin);
    }

    private static BaseContainer? KeepCore(
        AbstractLevel level,
        Chunk chunk,
        BlockEntity blockEntity,
        PluginContainer plugin)
    {
        var position = blockEntity.BlockPosition;
        if (!ReferenceEquals(chunk.GetBlockEntity(position.X, position.Y, position.Z), blockEntity))
            return null;

        // A container stops taking clicks under its lock before its last save, so that the save has all the items
        // players moved and no click moves one after it (or out of an inventory the plugin's reload recreates).
        BaseContainer? retired = null;
        if (blockEntity is ContainerBlockEntity container)
        {
            using (container.Inventory.Lock())
            {
                container.Inventory.TryRetire();
                blockEntity.IsRemoved = true;
            }

            retired = container.Inventory;
        }

        var kept = new DataBlockEntity
        {
            Id = blockEntity.Id,
            BlockPosition = blockEntity.BlockPosition
        };

        blockEntity.SavedData = SavedData(blockEntity, plugin);
        var saved = BlockEntityNbt.Save(blockEntity) ?? new NbtCompound();
        foreach (var name in new[] { "id", "x", "y", "z", "keepPacked" })
            saved.Remove(name);

        kept.Load(saved);
        level.AttachBlockEntity(kept);

        if (!chunk.ReplaceBlockEntity(blockEntity, kept))
            return retired;

        blockEntity.IsRemoved = true;
        blockEntity.Host = null;
        blockEntity.Owner = null;

        return retired;
    }

    /// <summary>
    /// Closes the windows players have open on <paramref name="retired"/> inventories, and on containers of
    /// <paramref name="plugin"/>'s own classes, which would keep its load context alive, as it unloads.
    /// </summary>
    public static async ValueTask CloseWindowsAsync(IServer server, PluginContainer plugin, List<BaseContainer> retired)
    {
        foreach (var player in server.OnlinePlayers.Values.OfType<Player>())
        {
            if (player.OpenedContainer is not BaseContainer open)
                continue;

            var owned = PluginLoadContext.OwnerOf(open.GetType().Assembly) == plugin;
            if (owned || retired.Any(open.SharesSlotsWith))
                await player.CloseContainerAsync(plugin);
        }
    }

    private static IEnumerable<Chunk> ResidentChunks(AbstractLevel level) => level.Regions.Values
        .OfType<Region>()
        .SelectMany(region => region.ResidentChunks())
        .OfType<Chunk>();

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
