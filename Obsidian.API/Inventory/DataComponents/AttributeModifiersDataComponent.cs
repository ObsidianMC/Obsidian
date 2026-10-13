namespace Obsidian.API.Inventory.DataComponents;

/// <summary>Attribute modifiers and the 1.21.11 per-modifier display override.</summary>
public sealed record AttributeModifiersDataComponent() : SimpleDataComponent<ImmutableArray<AttributeModifier>>(
    DataComponentType.AttributeModifiers, "minecraft:attribute_modifiers", (_, _) => { }, _ => [])
{
    private ImmutableArray<(int Type, ChatMessage? Text)> displays = [];

    public ImmutableArray<(int Type, ChatMessage? Text)> Displays
    {
        get => this.displays.Select(page => (page.Type, ComponentCopies.Chat(page.Text))).ToImmutableArray();
        init => this.displays = value.Select(page => (page.Type, ComponentCopies.Chat(page.Text))).ToImmutableArray();
    }

    protected override void ReadCore(INetStreamReader reader)
    {
        var entries = reader.ReadLengthPrefixedArray(() =>
        {
            var modifier = new AttributeModifier
            {
                Id = reader.ReadRegistryId(ComponentValueCodecs.Attributes),
                Uuid = Guid.Empty,
                Name = reader.ReadString(),
                Value = reader.ReadDouble(),
                Operation = reader.ReadVarInt<AttributeOperation>(),
                Slot = reader.ReadVarInt<AttributeSlot>()
            };
            var display = reader.ReadVarInt();
            return (modifier, display, text: display == 2 ? reader.ReadChat() : null);
        }).ToImmutableArray();
        this.value = entries.Select(entry => entry.modifier).ToImmutableArray();
        this.displays = entries.Select(entry => (entry.display, entry.text)).ToImmutableArray();
    }

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.Value.Length);
        for (var i = 0; i < this.Value.Length; i++)
        {
            var modifier = this.Value[i];
            writer.WriteRegistryId(ComponentValueCodecs.Attributes, modifier.Id);
            writer.WriteString(modifier.Name);
            writer.WriteDouble(modifier.Value);
            writer.WriteVarInt(modifier.Operation);
            writer.WriteVarInt(modifier.Slot);

            var display = i < this.displays.Length ? this.displays[i] : default;
            writer.WriteVarInt(display.Type);
            if (display.Type == 2)
                writer.WriteChat(display.Text!);
        }
    }
}
