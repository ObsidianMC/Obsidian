namespace Obsidian.API.Inventory;
/// <summary>
/// A change an item makes to its holder's attribute while it's in <see cref="Slot"/>, like vanilla's attribute modifier
/// entries.
/// </summary>
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

    /// <summary>
    /// Gets how much the modifier changes the attribute, as <see cref="Operation"/> applies it.
    /// </summary>
    public required double Value { get; init; }

    /// <summary>
    /// Gets how <see cref="Value"/> is applied to the attribute.
    /// </summary>
    public required AttributeOperation Operation { get; init; }

    /// <summary>
    /// Gets the equipment slots the item must be in for the modifier to apply.
    /// </summary>
    public required AttributeSlot Slot { get; init; }

    /// <summary>
    /// Reads a modifier written by <see cref="Write"/>.
    /// </summary>
    public static AttributeModifier Read(INetStreamReader reader) => new()
    {
        Id = reader.ReadInt(),
        Uuid = reader.ReadGuid(),
        Name = reader.ReadString(),
        Value = reader.ReadDouble(),
        Operation = reader.ReadVarInt<AttributeOperation>(),
        Slot = reader.ReadVarInt<AttributeSlot>()
    };

    /// <summary>
    /// Writes the modifier's id, UUID, name, value, operation and slot.
    /// </summary>
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

/// <summary>
/// The equipment slots an attribute modifier applies in (vanilla's equipment slot groups).
/// </summary>
public enum AttributeSlot
{
    /// <summary>
    /// Any slot.
    /// </summary>
    Any,
    /// <summary>
    /// The main hand.
    /// </summary>
    MainHand,
    /// <summary>
    /// The off hand.
    /// </summary>
    OffHand,
    /// <summary>
    /// Either hand.
    /// </summary>
    Hand,
    /// <summary>
    /// The feet slot.
    /// </summary>
    Feet,
    /// <summary>
    /// The legs slot.
    /// </summary>
    Legs,
    /// <summary>
    /// The chest slot.
    /// </summary>
    Chest,
    /// <summary>
    /// The head slot.
    /// </summary>
    Head,
    /// <summary>
    /// Any of the four armour slots.
    /// </summary>
    Armor,
    /// <summary>
    /// An animal's body slot, like a horse's armour.
    /// </summary>
    Body
}

/// <summary>
/// How an attribute modifier's value is applied.
/// </summary>
public enum AttributeOperation : int
{
    /// <summary>
    /// Adds the value to the attribute's base (vanilla's <c>add_value</c>).
    /// </summary>
    Add,
    /// <summary>
    /// Adds the value times the attribute's base (vanilla's <c>add_multiplied_base</c>).
    /// </summary>
    MulBase,
    /// <summary>
    /// Multiplies the total by one plus the value (vanilla's <c>add_multiplied_total</c>).
    /// </summary>
    MulTotal
}

