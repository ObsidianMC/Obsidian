namespace Obsidian.API.ItemComponents;
public sealed class MaxStackSizeItemComponent : IItemComponent
{
    public ItemComponentType Type => ItemComponentType.MaxStackSize;

    public string Identifier => "minecraft:max_stack_size";

    public int MaxStackSize { get; set; }

    public void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.MaxStackSize);
    }

    public void Read(INetStreamReader reader)
    {
        this.MaxStackSize = reader.ReadVarInt();
    }
}
