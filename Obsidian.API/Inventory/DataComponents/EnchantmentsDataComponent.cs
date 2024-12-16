namespace Obsidian.API.Inventory.DataComponents;
public sealed class EnchantmentsDataComponent : IDataComponent
{
    public DataComponentType Type => DataComponentType.Enchantments;

    public string Identifier => "minecraft:enchantments";

    public List<Enchantment> Enchantments { get; set; } = [];

    public bool ShowInToolTip { get; set; }

    public void Read(INetStreamReader reader)
    {
        var count = reader.ReadVarInt();

        var enchantments = new List<Enchantment>(count);
        for (int i = 0; i < count; i++)
            enchantments[i] = reader.ReadEnchantment();

        this.Enchantments = enchantments;
    }

    public void Write(INetStreamWriter writer) => writer.WriteLengthPrefixedArray(this.ShowInToolTip, this.Enchantments);
}
