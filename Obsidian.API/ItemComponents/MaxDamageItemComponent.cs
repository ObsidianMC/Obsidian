namespace Obsidian.API.ItemComponents;
public sealed class MaxDamageItemComponent : IItemComponent
{
    public ItemComponentType Type => ItemComponentType.MaxStackSize;

    public string Identifier => "minecraft:max_damage";

    public required int MaxDamage { get; set; }

    public void Write(INetStreamWriter writer) =>
        writer.WriteVarInt(this.MaxDamage);

    public void Read(INetStreamReader reader) =>
        this.MaxDamage = reader.ReadVarInt();
}
