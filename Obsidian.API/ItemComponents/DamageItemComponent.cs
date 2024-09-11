namespace Obsidian.API.ItemComponents;
public sealed class DamageItemComponent : IItemComponent
{
    public ItemComponentType Type => ItemComponentType.MaxStackSize;

    public string Identifier => "minecraft:damage";

    public required int Damage { get; set; }

    public void Write(INetStreamWriter writer) =>
        writer.WriteVarInt(this.Damage);

    public void Read(INetStreamReader reader) =>
        this.Damage = reader.ReadVarInt();
}
