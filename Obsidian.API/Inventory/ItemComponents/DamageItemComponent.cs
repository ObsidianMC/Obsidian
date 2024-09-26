namespace Obsidian.API.ItemComponents;
public sealed class DamageItemComponent : IItemComponent
{
    public ItemComponentType Type => ItemComponentType.Damage;

    public string Identifier => "minecraft:damage";

    public int Damage { get; set; }

    public void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.Damage);
    }

    public void Read(INetStreamReader reader)
    {
        this.Damage = reader.ReadVarInt();
    }
}
