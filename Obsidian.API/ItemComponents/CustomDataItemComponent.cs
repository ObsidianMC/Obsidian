namespace Obsidian.API.ItemComponents;
public abstract class CustomDataItemComponent : IItemComponent
{
    public ItemComponentType Type => ItemComponentType.CustomData;

    public virtual string Identifier => "minecraft:custom_data";

    public abstract void Write(INetStreamWriter writer);
    public abstract void Read(INetStreamReader reader);
}
