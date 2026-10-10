namespace Obsidian.API.Inventory.DataComponents;

/// <summary>Attribute modifiers and the 1.21.11 per-modifier display override.</summary>
public sealed record AttributeModifiersDataComponent() : SimpleDataComponent<AttributeModifier[]>(
    DataComponentType.AttributeModifiers, "minecraft:attribute_modifiers", (_, _) => { }, _ => [])
{
    public (int Type, ChatMessage? Text)[] Displays { get; set; } = [];

    public override void Read(INetStreamReader reader)
    {
        var entries = reader.ReadLengthPrefixedArray(() =>
        {
            var modifier = new AttributeModifier
            {
                Id = reader.ReadVarInt(),
                Uuid = Guid.Empty,
                Name = reader.ReadString(),
                Value = reader.ReadDouble(),
                Operation = reader.ReadVarInt<AttributeOperation>(),
                Slot = reader.ReadVarInt<AttributeSlot>()
            };
            var display = reader.ReadVarInt();
            return (modifier, display, text: display == 2 ? reader.ReadChat() : null);
        });
        this.Value = entries.Select(entry => entry.modifier).ToArray();
        this.Displays = entries.Select(entry => (entry.display, entry.text)).ToArray();
    }

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.Value.Length);
        for (var i = 0; i < this.Value.Length; i++)
        {
            var modifier = this.Value[i];
            writer.WriteVarInt(modifier.Id);
            writer.WriteString(modifier.Name);
            writer.WriteDouble(modifier.Value);
            writer.WriteVarInt(modifier.Operation);
            writer.WriteVarInt(modifier.Slot);

            var display = i < this.Displays.Length ? this.Displays[i] : default;
            writer.WriteVarInt(display.Type);
            if (display.Type == 2)
                writer.WriteChat(display.Text!);
        }
    }
}
