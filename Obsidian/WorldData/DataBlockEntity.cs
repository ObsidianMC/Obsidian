using Obsidian.Nbt;

namespace Obsidian.WorldData;

/// <summary>
/// A block entity kept as vanilla's saved data, for block entities Obsidian has no behavior for yet: spawners, end
/// gateways, chests waiting for their loot and the like.
/// </summary>
/// <remarks>
/// <see cref="Data"/> holds the fields vanilla saves besides the id and position, e.g. <c>LootTable</c> and
/// <c>LootTableSeed</c> for a chest, or <c>SpawnData</c> for a spawner. Missing fields mean vanilla's defaults.
/// </remarks>
public sealed class DataBlockEntity : IBlockEntity
{
    // Block entity types whose getUpdateTag sends data to clients; the others send nothing.
    private static readonly HashSet<string> clientDataTypes =
    [
        "minecraft:banner", "minecraft:beacon", "minecraft:brushable_block", "minecraft:campfire", "minecraft:conduit",
        "minecraft:creaking_heart", "minecraft:decorated_pot", "minecraft:end_gateway", "minecraft:hanging_sign", "minecraft:jigsaw",
        "minecraft:mob_spawner", "minecraft:shelf", "minecraft:sign", "minecraft:skull", "minecraft:structure_block",
        "minecraft:test_block", "minecraft:test_instance_block", "minecraft:trial_spawner", "minecraft:vault"
    ];

    /// <summary>
    /// The block entity type, e.g. <c>minecraft:mob_spawner</c>.
    /// </summary>
    public required string Id { get; init; }

    public required Vector BlockPosition { get; init; }

    public NbtCompound Data { get; init; } = new();

    /// <summary>
    /// Sets a field of <see cref="Data"/>, replacing any previous value.
    /// </summary>
    public void Set<T>(string name, T value) => this.Set(new NbtTag<T>(name, value));

    /// <summary>
    /// Sets a field of <see cref="Data"/> to a named tag, replacing any previous value.
    /// </summary>
    public void Set(INbtTag tag)
    {
        this.Data.Remove(tag.Name!);
        this.Data.Add(tag.Name!, tag);
    }

    public void ToNbt() => throw new NotSupportedException("Use Data instead.");

    public void FromNbt() => throw new NotSupportedException("Use Data instead.");

    /// <summary>
    /// The data clients get in chunk packets (vanilla's <c>getUpdateTag</c>), or <c>null</c> when the type sends none.
    /// Loot tables and spawn potentials stay on the server.
    /// </summary>
    public NbtCompound? GetClientData()
    {
        if (!clientDataTypes.Contains(this.Id))
            return null;

        var data = new NbtCompound();
        foreach (var (name, tag) in this.Data)
        {
            if (name is not ("LootTable" or "LootTableSeed" or "SpawnPotentials"))
                data.Add(name, tag);
        }

        return data.Count == 0 ? null : data;
    }

    /// <summary>
    /// Keeps a chunk's block entities in step with a block written during generation, like vanilla's
    /// <c>WorldGenRegion.setBlock</c>: a block with a block entity gets an empty one (its defaults) unless it already has one,
    /// and other blocks drop the block entity of the block they replace.
    /// </summary>
    internal static void ApplyBlockChange(IChunk chunk, Vector position, IBlock block)
    {
        // The flag is much cheaper than the type lookup, and blocks without it have no type.
        var type = block.HasBlockEntity() ? block.BlockEntityType() : null;
        if (type is null)
            chunk.RemoveBlockEntity(position.X, position.Y, position.Z);
        else if (chunk.GetBlockEntity(position.X, position.Y, position.Z)?.Id != type)
            chunk.SetBlockEntity(position.X, position.Y, position.Z, new DataBlockEntity { Id = type, BlockPosition = position });
    }

    public IBlockEntity Clone() => new DataBlockEntity { Id = this.Id, BlockPosition = this.BlockPosition, Data = Copy(this.Data) };

    /// <summary>
    /// Deep copies a compound.
    /// </summary>
    private static NbtCompound Copy(NbtCompound compound)
    {
        var copy = new NbtCompound(compound.Name ?? string.Empty);
        foreach (var (name, tag) in compound)
            copy.Add(name, Copy(tag));

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
