using Obsidian.Nbt;

namespace Obsidian.API.Inventory.DataComponents;

/// <summary>Entity or block-entity type registry ID followed by its custom NBT.</summary>
public sealed record EntityDataComponent(DataComponentType ComponentType) : DataComponent
{
    public override DataComponentType Type => this.ComponentType;
    public override string Identifier => OpaqueDataComponent.GetIdentifier(this.Type);
    public int RegistryId { get; set; }
    public NbtCompound Data { get; set; } = new();
    public override void Read(INetStreamReader reader) { this.RegistryId = reader.ReadVarInt(); this.Data = reader.ReadNbtCompound(); }
    public override void Write(INetStreamWriter writer) { writer.WriteVarInt(this.RegistryId); writer.WriteNbtCompound(this.Data); }
}

public sealed record BeesDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.Bees;
    public override string Identifier => "minecraft:bees";
    public (EntityDataComponent Entity, int TicksInHive, int MinTicksInHive)[] Occupants { get; set; } = [];
    public override void Read(INetStreamReader reader) => this.Occupants = reader.ReadLengthPrefixedArray(() =>
    {
        var entity = new EntityDataComponent(DataComponentType.EntityData); entity.Read(reader);
        return (entity, reader.ReadVarInt(), reader.ReadVarInt());
    });
    public override void Write(INetStreamWriter writer) => writer.WriteLengthPrefixedArray(occupant =>
    {
        occupant.Entity.Write(writer); writer.WriteVarInt(occupant.TicksInHive); writer.WriteVarInt(occupant.MinTicksInHive);
    }, this.Occupants);
}
