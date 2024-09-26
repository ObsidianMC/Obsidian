namespace Obsidian.API.ItemComponents;
public sealed class MaxDamageItemComponent : IItemComponent
{
    public ItemComponentType Type => ItemComponentType.MaxDamage;

    public string Identifier => "minecraft:max_damage";

    public int MaxDamage { get; set; }

    public MaxDamageItemComponent(int maxDamage)
    {
        this.MaxDamage = maxDamage;
    }

    public MaxDamageItemComponent() { }

    public void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.MaxDamage);
    }

    public void Read(INetStreamReader reader)
    {
        this.MaxDamage = reader.ReadVarInt();
    }
}
