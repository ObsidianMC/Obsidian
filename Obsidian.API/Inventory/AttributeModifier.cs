namespace Obsidian.API.Inventory;
public readonly record struct AttributeModifier : INetworkSerializable<AttributeModifier>
{
    /// <summary>
    /// The attribute's protocol id (see <see cref="AttributeIds"/>).
    /// </summary>
    public required int Id { get; init; }

    /// <summary>
    /// The modifier's legacy UUID, which 1.21.11's components don't carry.
    /// </summary>
    public Guid Uuid { get; init; }

    /// <summary>
    /// The modifier's identifier, like <c>minecraft:armor.helmet</c>, unique among the holder's modifiers of the
    /// attribute.
    /// </summary>
    public required string Name { get; init; }

    public required double Value { get; init; }

    public required AttributeOperation Operation { get; init; }

    public required AttributeSlot Slot { get; init; }

    public static AttributeModifier Read(INetStreamReader reader) => new()
    {
        Id = reader.ReadInt(),
        Uuid = reader.ReadGuid(),
        Name = reader.ReadString(),
        Value = reader.ReadDouble(),
        Operation = reader.ReadVarInt<AttributeOperation>(),
        Slot = reader.ReadVarInt<AttributeSlot>()
    };

    public static void Write(AttributeModifier value, INetStreamWriter writer)
    {
        writer.WriteVarInt(value.Id);
        writer.WriteUuid(value.Uuid);
        writer.WriteString(value.Name);
        writer.WriteDouble(value.Value);
        writer.WriteVarInt(value.Operation);
        writer.WriteVarInt(value.Slot);
    }
}

public enum AttributeSlot
{
    Any,
    MainHand,
    OffHand,
    Hand,
    Feet,
    Legs,
    Chest,
    Head,
    Armor,
    Body
}

public enum AttributeOperation : int
{
    Add,
    MulBase,
    MulTotal
}

