using Obsidian.Nbt;

namespace Obsidian.API.Inventory.DataComponents;

/// <summary>Entity or block-entity type registry ID followed by its custom NBT.</summary>
public sealed record EntityDataComponent(DataComponentType ComponentType) : DataComponent
{
    public override DataComponentType Type => this.ComponentType;
    public override string Identifier => OpaqueDataComponent.GetIdentifier(this.Type);
    public int RegistryId { get; set; }
    public NbtCompound Data { get; set; } = new();

    // The registry RegistryId belongs to: entity types, or block entity types for minecraft:block_entity_data.
    private string Registry => this.ComponentType == DataComponentType.BlockEntityData
        ? ComponentValueCodecs.BlockEntityTypes
        : ComponentValueCodecs.EntityTypes;

    public override void Read(INetStreamReader reader)
    {
        this.RegistryId = reader.ReadRegistryId(this.Registry);
        this.Data = reader.ReadNbtCompound();
    }

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteRegistryId(this.Registry, this.RegistryId);
        writer.WriteNbtCompound(this.Data);
    }
}

public sealed record BeesDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.Bees;
    public override string Identifier => "minecraft:bees";
    public (EntityDataComponent Entity, int TicksInHive, int MinTicksInHive)[] Occupants { get; set; } = [];

    public override void Read(INetStreamReader reader) => this.Occupants = reader.ReadLengthPrefixedArray(() =>
    {
        var entity = new EntityDataComponent(DataComponentType.EntityData);
        entity.Read(reader);
        return (entity, reader.ReadVarInt(), reader.ReadVarInt());
    });

    public override void Write(INetStreamWriter writer) => writer.WriteLengthPrefixedArray(occupant =>
    {
        occupant.Entity.Write(writer);
        writer.WriteVarInt(occupant.TicksInHive);
        writer.WriteVarInt(occupant.MinTicksInHive);
    }, this.Occupants);
}
