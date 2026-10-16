using Obsidian.Nbt;

namespace Obsidian.API.Inventory.DataComponents;

/// <summary>Entity or block-entity type registry ID followed by its custom NBT.</summary>
public sealed record EntityDataComponent(DataComponentType ComponentType) : DataComponent
{
    public override DataComponentType Type => this.ComponentType;
    public override string Identifier => OpaqueDataComponent.GetIdentifier(this.Type);
    private int registryId;

    public int RegistryId
    {
        get => this.registryId;
        init => this.registryId = value;
    }

    private NbtCompound data = new();

    public NbtCompound Data
    {
        get => ComponentCopies.Nbt(this.data)!;
        init => this.data = ComponentCopies.Nbt(value)!;
    }

    // The registry RegistryId belongs to: entity types, or block entity types for minecraft:block_entity_data.
    private string Registry => this.ComponentType == DataComponentType.BlockEntityData
        ? ComponentValueCodecs.BlockEntityTypes
        : ComponentValueCodecs.EntityTypes;

    protected override void ReadCore(INetStreamReader reader)
    {
        this.registryId = reader.ReadRegistryId(this.Registry);
        this.data = reader.ReadNbtCompound();
    }

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteRegistryId(this.Registry, this.RegistryId);
        writer.WriteNbtCompound(this.data);
    }
}

public sealed record BeesDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.Bees;
    public override string Identifier => "minecraft:bees";
    private ImmutableArray<(EntityDataComponent Entity, int TicksInHive, int MinTicksInHive)> occupants = [];

    public ImmutableArray<(EntityDataComponent Entity, int TicksInHive, int MinTicksInHive)> Occupants
    {
        get => this.occupants;
        init => this.occupants = value;
    }

    protected override void ReadCore(INetStreamReader reader) => this.occupants = reader.ReadLengthPrefixedArray(() =>
    {
        var entity = new EntityDataComponent(DataComponentType.EntityData);
        entity = (EntityDataComponent)entity.Read(reader);
        return (entity, reader.ReadVarInt(), reader.ReadVarInt());
    }).ToImmutableArray();

    public override void Write(INetStreamWriter writer) => writer.WriteLengthPrefixedArray(occupant =>
    {
        occupant.Entity.Write(writer);
        writer.WriteVarInt(occupant.TicksInHive);
        writer.WriteVarInt(occupant.MinTicksInHive);
    }, this.Occupants.AsSpan());
}
