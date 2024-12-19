namespace Obsidian.API.Inventory.DataComponents;
public sealed class EnchantmentsDataComponent : IDataComponent
{
    public DataComponentType Type => DataComponentType.Enchantments;

    public string Identifier => "minecraft:enchantments";

    public List<Enchantment> Enchantments { get; set; } = [];

    public bool ShowInToolTip { get; set; }

    public void Read(INetStreamReader reader) => this.Enchantments = reader.ReadLengthPrefixedArray(reader.ReadEnchantment);

    public void Write(INetStreamWriter writer) => writer.WriteLengthPrefixedArray(this.ShowInToolTip, this.Enchantments);
}
