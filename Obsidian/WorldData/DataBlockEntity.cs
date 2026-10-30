using Obsidian.API.BlockEntities;
using Obsidian.Nbt;
using Obsidian.Net;

namespace Obsidian.WorldData;

/// <summary>
/// A block entity kept as vanilla's saved data: spawners, end gateways, beehives and the like, which the server reads
/// and changes as data, and block entities of types the server can't create now, like a plugin's while the plugin is
/// gone, which are saved again as they were loaded.
/// </summary>
/// <remarks>
/// <see cref="Data"/> holds the fields vanilla saves besides the id and position, e.g. <c>SpawnData</c> for a spawner.
/// Missing fields mean vanilla's defaults.
/// </remarks>
public sealed class DataBlockEntity : BlockEntity
{
    // Block entity types whose getUpdateTag sends data to clients; the others send nothing.
    private static readonly HashSet<string> clientDataTypes =
    [
        "minecraft:banner", "minecraft:beacon", "minecraft:brushable_block", "minecraft:campfire",
        "minecraft:conduit", "minecraft:creaking_heart", "minecraft:decorated_pot", "minecraft:end_gateway",
        "minecraft:hanging_sign", "minecraft:jigsaw", "minecraft:mob_spawner", "minecraft:shelf", "minecraft:sign",
        "minecraft:skull", "minecraft:structure_block", "minecraft:test_block", "minecraft:test_instance_block",
        "minecraft:trial_spawner", "minecraft:vault"
    ];

    // The types kept as data whose saved items vanilla drops when the block is removed: those whose
    // preRemoveSideEffects calls Containers.dropContents.
    private static readonly HashSet<string> droppingTypes =
    [
        "minecraft:campfire", "minecraft:chiseled_bookshelf", "minecraft:crafter"
    ];

    public NbtCompound Data { get; init; } = new();

    // Spawner timers change on the level's thread under the data's lock, while chunks are saved or sent in parallel.
    internal override bool SavesOnAnyThread => true;

    /// <summary>
    /// Whether the saved <c>Items</c> drop when the block is removed, like vanilla's for the type.
    /// </summary>
    internal bool DropsItems => droppingTypes.Contains(this.Id);

    /// <summary>Reconstructs only host-owned container semantics while the plugin is absent.</summary>
    internal ContainerBlockEntity? KeptContainer()
    {
        var data = this.SnapshotData();
        if (!data.TryGetTag<NbtCompound>("obsidian:container", out var policy)
            || !policy.TryGetTagValue<int>("size", out var size)
            || size < 0 || size > short.MaxValue)
        {
            return null;
        }

        var container = new ContainerBlockEntity(new Container(size))
        {
            Id = this.Id,
            BlockPosition = this.BlockPosition,
            // Without the live block entity, its plugin cannot promise a content-keeping loot drop. Spill instead,
            // and expose no container component to block loot, so an available table cannot also copy the items.
            DropsContents = true
        };
        container.Load(data);
        return container;
    }

    /// <summary>
    /// Sets a field of <see cref="Data"/>, replacing any previous value.
    /// </summary>
    public void Set<T>(string name, T value) => this.Set(new NbtTag<T>(name, value));

    /// <summary>
    /// Sets a field of <see cref="Data"/> to a named tag, replacing any previous value.
    /// </summary>
    public void Set(INbtTag tag)
    {
        lock (this.Data)
        {
            this.Data.Remove(tag.Name!);
            this.Data.Add(tag.Name!, tag);
        }
    }

    /// <summary>
    /// Removes a field of <see cref="Data"/>, which then means vanilla's default.
    /// </summary>
    public void Remove(string name)
    {
        lock (this.Data)
            this.Data.Remove(name);
    }

    protected internal override void Load(NbtCompound tag)
    {
        lock (this.Data)
        {
            this.Data.Clear();
            foreach (var (name, child) in tag)
                this.Data.Add(name, Copy(child));
        }
    }

    protected internal override void Save(NbtCompound tag)
    {
        foreach (var (name, child) in this.SnapshotData())
            tag.Add(name, child);
    }

    /// <summary>
    /// The data clients get (vanilla's <c>getUpdateTag</c>), or <c>null</c> when the type sends none. Loot tables and
    /// spawn potentials stay on the server.
    /// </summary>
    protected internal override NbtCompound? GetUpdateData()
    {
        if (!clientDataTypes.Contains(this.Id))
            return null;

        var data = new NbtCompound();
        foreach (var (name, tag) in this.SnapshotData())
        {
            if (name is not ("LootTable" or "LootTableSeed" or "SpawnPotentials"))
                data.Add(name, tag);
        }

        NetworkBuffer.ProjectUpdateItems(data);
        return data.Count == 0 ? null : data;
    }

    /// <summary>
    /// Keeps a chunk's block entities in step with a block written during generation, like vanilla's
    /// <c>WorldGenRegion.setBlock</c>: a block with a block entity gets a new one (its defaults) unless it already
    /// has one of its type, and other blocks drop the block entity of the block they replace, without the side
    /// effects of removing it. A plugin's type is kept as data until the chunk is complete (see
    /// <see cref="BlockEntityNbt.Create"/>).
    /// </summary>
    internal static void ApplyBlockChange(IChunk chunk, Vector position, IBlock block)
    {
        // The flag is much cheaper than the type lookup, and blocks without it have no type.
        var type = block.HasBlockEntity() ? block.BlockEntityType() : null;
        if (type is null)
        {
            chunk.RemoveBlockEntity(position.X, position.Y, position.Z);
        }
        else if (chunk.GetBlockEntity(position.X, position.Y, position.Z)?.Id != type)
        {
            var created = BlockEntityNbt.Create(type, position, pluginInstances: false);
            chunk.SetBlockEntity(position.X, position.Y, position.Z, created);
        }
    }

    internal NbtCompound SnapshotData()
    {
        lock (this.Data)
            return Copy(this.Data);
    }

    /// <summary>
    /// A deep copy of the field of <see cref="Data"/> named <paramref name="name"/>, or null without one of that type.
    /// </summary>
    internal T? SnapshotTag<T>(string name) where T : class, INbtTag
    {
        lock (this.Data)
            return this.Data.TryGetTag<T>(name, out var tag) ? (T)Copy(tag) : null;
    }

    /// <summary>
    /// Deep copies a compound.
    /// </summary>
    internal static NbtCompound Copy(NbtCompound compound)
    {
        var copy = new NbtCompound(compound.Name ?? string.Empty);
        foreach (var (name, tag) in compound)
            copy.SetReadTag(name, Copy(tag));

        return copy;
    }

    private static INbtTag Copy(INbtTag tag)
    {
        switch (tag)
        {
            case NbtCompound compound:
                return Copy(compound);
            case NbtList list:
                var listCopy = new NbtList(list.ListType, list.Name ?? string.Empty);
                foreach (var child in list)
                    listCopy.Add(Copy(child));

                return listCopy;
            case NbtArray<byte> bytes:
                return new NbtArray<byte>(bytes.Name, [.. bytes.GetArray()]);
            case NbtArray<int> ints:
                return new NbtArray<int>(ints.Name, [.. ints.GetArray()]);
            case NbtArray<long> longs:
                return new NbtArray<long>(longs.Name, [.. longs.GetArray()]);
            default:
                // Value tags are immutable.
                return tag;
        }
    }
}
