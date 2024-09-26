namespace Obsidian.API.ItemComponents;

public abstract class NamedItemComponent : IItemComponent
{
    public ChatMessage Name { get; set; } = default!;

    public abstract ItemComponentType Type { get; }

    public abstract string Identifier { get; }

    public void Write(INetStreamWriter writer)
    {
        writer.WriteChat(this.Name);
    }

    public void Read(INetStreamReader reader)
    {
        this.Name = reader.ReadChat();
    }
}

public sealed class ItemNameItemComponent : NamedItemComponent
{
    public override ItemComponentType Type => ItemComponentType.ItemName;

    public override string Identifier => "minecraft:item_name";
}

public sealed class CustomNameItemComponent : NamedItemComponent
{
    public override ItemComponentType Type => ItemComponentType.CustomName;

    public override string Identifier => "minecraft:custom_name";
}
