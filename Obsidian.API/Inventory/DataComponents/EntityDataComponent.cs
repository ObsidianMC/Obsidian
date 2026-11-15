using Obsidian.Nbt;

namespace Obsidian.API.Inventory.DataComponents;

/// <summary>Entity or block-entity type registry ID followed by its custom NBT.</summary>
public sealed record EntityDataComponent(DataComponentType ComponentType) : DataComponent
{
    private ComponentValueKey Comparison => new(new object?[]
    {
        this.ComponentType,
        this.registryId,
        this.data
    });

    /// <inheritdoc/>
    public bool Equals(EntityDataComponent? other) => ReferenceEquals(this, other)
        || other is not null && base.Equals(other) && this.Comparison.Equals(other.Comparison);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(base.GetHashCode(), this.Comparison);

    /// <inheritdoc/>
    public override DataComponentType Type => this.ComponentType;
    /// <inheritdoc/>
    public override string Identifier => OpaqueDataComponent.GetIdentifier(this.Type);
    private int registryId;

    /// <summary>
    /// Gets the type's id in the server's entity type registry, or its block entity type registry for
    /// <c>minecraft:block_entity_data</c>. It's translated from and to each connection's ids when the component is
    /// read or written.
    /// </summary>
    public int RegistryId
    {
        get => this.registryId;
        init => this.registryId = value;
    }

    private NbtCompound data = new();

    /// <summary>
    /// Gets the entity's or block entity's saved data, without its id; a copy on each get and set.
    /// </summary>
    public NbtCompound Data
    {
        get => ComponentCopies.Nbt(this.data)!;
        init => this.data = ComponentCopies.Nbt(value)!;
    }

    // The registry RegistryId belongs to: entity types, or block entity types for minecraft:block_entity_data.
    private string Registry => this.ComponentType == DataComponentType.BlockEntityData
        ? ComponentValueCodecs.BlockEntityTypes
        : ComponentValueCodecs.EntityTypes;

    /// <inheritdoc/>
    protected override void ReadCore(INetStreamReader reader)
    {
        this.registryId = reader.ReadRegistryId(this.Registry);
        this.data = reader.ReadNbtCompound();
    }

    /// <inheritdoc/>
    public override void Write(INetStreamWriter writer)
    {
        writer.WriteRegistryId(this.Registry, this.RegistryId);
        writer.WriteNbtCompound(this.data);
    }
}

/// <summary>
/// The bees inside a beehive or bee nest item (<c>minecraft:bees</c>).
/// </summary>
public sealed record BeesDataComponent : DataComponent
{
    private ComponentValueKey Comparison => new(new object?[]
    {
        ComponentValueKey.Sequence(this.occupants)
    });

    /// <inheritdoc/>
    public bool Equals(BeesDataComponent? other) => ReferenceEquals(this, other)
        || other is not null && base.Equals(other) && this.Comparison.Equals(other.Comparison);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(base.GetHashCode(), this.Comparison);

    /// <inheritdoc/>
    public override DataComponentType Type => DataComponentType.Bees;
    /// <inheritdoc/>
    public override string Identifier => "minecraft:bees";
    private ImmutableArray<(EntityDataComponent Entity, int TicksInHive, int MinTicksInHive)> occupants = [];

    /// <summary>
    /// Gets each bee's data with how long it has been in the hive and the least time it stays there, in ticks.
    /// </summary>
    public ImmutableArray<(EntityDataComponent Entity, int TicksInHive, int MinTicksInHive)> Occupants
    {
        get => this.occupants;
        init => this.occupants = value;
    }

    /// <inheritdoc/>
    protected override void ReadCore(INetStreamReader reader) => this.occupants = reader.ReadLengthPrefixedArray(() =>
    {
        var entity = new EntityDataComponent(DataComponentType.EntityData);
        entity = (EntityDataComponent)entity.Read(reader);
        return (entity, reader.ReadVarInt(), reader.ReadVarInt());
    }).ToImmutableArray();

    /// <inheritdoc/>
    public override void Write(INetStreamWriter writer) => writer.WriteLengthPrefixedArray(occupant =>
    {
        occupant.Entity.Write(writer);
        writer.WriteVarInt(occupant.TicksInHive);
        writer.WriteVarInt(occupant.MinTicksInHive);
    }, this.Occupants.AsSpan());
}
