namespace Obsidian.API.ItemComponents;

//Custom data is nbt data.
public sealed class CustomDataItemComponent : IItemComponent
{
    public ItemComponentType Type => ItemComponentType.CustomData;

    public string Identifier => "minecraft:custom_data";

    public void Read(INetStreamReader reader) { }
    public void Write(INetStreamWriter writer) { }
}
