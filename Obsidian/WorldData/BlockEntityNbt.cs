using Microsoft.Extensions.Logging;
using Obsidian.API.BlockEntities;
using Obsidian.Nbt;
using Obsidian.Plugins;

namespace Obsidian.WorldData;

/// <summary>
/// Creates block entities of a type, and saves and loads them in vanilla's form (the entries of a chunk's
/// <c>block_entities</c>: the type's <c>id</c>, the position and the block entity's own fields).
/// </summary>
internal static partial class BlockEntityNbt
{
    /// <summary>
    /// Saves a block entity with its id and position, or returns null for one that isn't saved: one that isn't a
    /// <see cref="BlockEntity"/>, or has no type.
    /// </summary>
    /// <remarks>
    /// Calls no plugin code: a plugin's block entity is saved as of the last change the level took in (see
    /// <see cref="BlockEntity.SavedData"/>).
    /// </remarks>
    public static NbtCompound? Save(IBlockEntity blockEntity)
    {
        if (blockEntity is not BlockEntity { Id.Length: > 0 } saved)
            return null;

        var tag = Header(saved.Id, saved.BlockPosition);
        var data = saved.SavesOnAnyThread ? SaveData(saved) : saved.SavedData;

        if (data is not null)
        {
            foreach (var (name, child) in DataBlockEntity.Copy(data))
                tag.Add(name, child);
        }

        return tag;
    }

    /// <summary>
    /// What a block entity's <see cref="BlockEntity.Save"/> writes, without the id and position.
    /// </summary>
    public static NbtCompound SaveData(BlockEntity blockEntity)
    {
        var tag = new NbtCompound();
        blockEntity.Save(tag);

        return tag;
    }

    /// <summary>
    /// Loads a saved block entity (see <see cref="Create"/>).
    /// </summary>
    public static BlockEntity Load(NbtCompound tag, string id, Vector position, bool pluginInstances = true)
    {
        var data = new NbtCompound();
        foreach (var (name, child) in tag)
        {
            if (name is not ("id" or "x" or "y" or "z" or "keepPacked"))
                data.Add(name, child);
        }

        return Create(id, position, data, pluginInstances: pluginInstances);
    }

    /// <summary>
    /// A new block entity of <paramref name="type"/> at <paramref name="position"/>, loaded from
    /// <paramref name="data"/> when given: the class of one of vanilla's types the server models (see
    /// <see cref="VanillaBlockEntities"/>), or of a plugin's while its plugin is loaded; or else a
    /// <see cref="DataBlockEntity"/> that keeps the data as it is.
    /// </summary>
    /// <param name="type">The block entity type's identifier.</param>
    /// <param name="position">Where its block is.</param>
    /// <param name="data">Its saved data, without the id and position; null for a new one.</param>
    /// <param name="logger">
    /// Where a plugin's block entity failing to be created or loaded is logged, if anywhere.
    /// </param>
    /// <param name="pluginInstances">
    /// False to keep a plugin's type as a <see cref="DataBlockEntity"/> too, for chunks still generating: those are
    /// outside the level's tick, which runs plugins' block entities, so they're created once the chunk is complete.
    /// </param>
    public static BlockEntity Create(
        string type,
        Vector position,
        NbtCompound? data = null,
        ILogger? logger = null,
        bool pluginInstances = true)
    {
        var blockEntity = VanillaBlockEntities.Create(type, position);
        if (blockEntity is not null)
        {
            blockEntity.Id = type;
            blockEntity.BlockPosition = position;

            if (data is not null)
                blockEntity.Load(data);

            return blockEntity;
        }

        if (pluginInstances
            && BlockEntityTypesRegistry.TryGet(type, out var pluginType)
            && pluginType.Binding is BlockEntityBinding binding
            && CreatePlugin(pluginType, binding, position, data, logger) is BlockEntity created)
        {
            return created;
        }

        var kept = new DataBlockEntity
        {
            Id = type,
            BlockPosition = position
        };

        if (data is not null)
            kept.Load(data);

        return kept;
    }

    /// <summary>
    /// Loads <paramref name="data"/> into a block entity that's already placed, like vanilla's
    /// <c>loadWithComponents</c> when a structure places a block entity with data. A plugin's block entity loads
    /// inside a call into its plugin, and saves what it loaded.
    /// </summary>
    public static void LoadInto(BlockEntity blockEntity, NbtCompound data)
    {
        if (blockEntity.Owner is not PluginContainer plugin)
        {
            blockEntity.Load(data);
            return;
        }

        using var call = plugin.TryEnter();
        if (call is null)
            return;

        blockEntity.Load(data);
        blockEntity.SavedData = DataBlockEntity.Copy(data);
        blockEntity.ClientData = CopyOrNull(blockEntity.GetUpdateData());
        blockEntity.ShapeData = blockEntity.GetShapes();
    }

    /// <summary>
    /// Gives a container block entity a loot table and the seed to roll it with, like vanilla's
    /// <c>RandomizableContainer.setLootTable</c>: one the server models, or one kept as data (a hopper's, say).
    /// </summary>
    public static void SetLootTable(IBlockEntity blockEntity, string lootTable, long seed)
    {
        switch (blockEntity)
        {
            case ContainerBlockEntity container:
                container.LootTable = lootTable;
                container.LootTableSeed = seed;
                break;
            case DataBlockEntity data:
                data.Set("LootTable", lootTable);

                // Like vanilla, a zero seed isn't saved.
                if (seed != 0L)
                    data.Set("LootTableSeed", seed);
                else
                    data.Remove("LootTableSeed");

                break;
        }
    }

    // Creates a plugin's block entity inside a call into the plugin, or returns null when the plugin is unloading or
    // its code fails.
    private static BlockEntity? CreatePlugin(
        BlockEntityType type,
        BlockEntityBinding binding,
        Vector position,
        NbtCompound? data,
        ILogger? logger)
    {
        var plugin = binding.Owner as PluginContainer;

        using var call = plugin?.TryEnter();
        if (plugin is not null && call is null)
            return null;

        try
        {
            var blockEntity = binding.Factory();
            blockEntity.Id = type.Identifier;
            blockEntity.BlockPosition = position;
            blockEntity.Owner = binding.Owner;

            if (data is not null)
            {
                blockEntity.Load(data);
                blockEntity.SavedData = DataBlockEntity.Copy(data);
            }

            blockEntity.ClientData = CopyOrNull(blockEntity.GetUpdateData());
            blockEntity.ShapeData = blockEntity.GetShapes();

            return blockEntity;
        }
        catch (Exception ex)
        {
            if (logger is not null)
                Log.CreateFailed(logger, ex, type.Identifier, position);

            return null;
        }
    }

    // Plugins' update data is copied as the server takes it, so that the plugin changing it later changes nothing sent.
    private static NbtCompound? CopyOrNull(NbtCompound? data) => data is null ? null : DataBlockEntity.Copy(data);

    private static NbtCompound Header(string id, Vector position) => new()
    {
        new NbtTag<string>("id", id),
        new NbtTag<int>("x", position.X),
        new NbtTag<int>("y", position.Y),
        new NbtTag<int>("z", position.Z),
        new NbtTag<bool>("keepPacked", false)
    };

    private static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Couldn't create block entity {Type} at {Position}; it's kept as saved")]
        public static partial void CreateFailed(ILogger logger, Exception exception, string type, Vector position);
    }
}
