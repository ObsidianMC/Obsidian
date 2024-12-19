namespace Obsidian.API.Inventory.DataComponents;
public sealed class AttributeModifierDataComponent : IDataComponent
{
    public DataComponentType Type => DataComponentType.AttributeModifiers;

    public string Identifier => "minecraft:attribute_modifiers";

    public required List<AttributeModifier> Attributes { get; set; }

    public bool ShowInTooltip { get; set; }

    public void Read(INetStreamReader reader)
    {
        var count = reader.ReadVarInt();

        var attributes = new List<AttributeModifier>(count);

        for (int i = 0; i < count; i++)
            attributes[i] = reader.ReadAttributeModifier();

        this.Attributes = attributes;
        this.ShowInTooltip = reader.ReadBoolean();
    }

    public void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(Attributes.Count);

        foreach (var attribute in Attributes)
        {
            writer.WriteAttributeModifier(attribute);
        }

        writer.WriteBoolean(ShowInTooltip);
    }
}

public readonly struct AttributeModifier : INetworkSerializable<AttributeModifier>
{
    public required int Id { get; init; }

    public required Guid Uuid { get; init; }

    public required string Name { get; init; }

    public required double Value { get; init; }

    public required AttributeOperation Operation { get; init; }

    public required AttributeSlot Slot { get; init; }

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
