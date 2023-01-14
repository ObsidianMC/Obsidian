namespace Obsidian.Registries;
public partial class ItemsRegistry
{
    public static Item Get(int id) => Items.Values.SingleOrDefault(x => x.Id == id);
    public static Item Get(Material mat) => Items.GetValueOrDefault(mat);
    public static Item Get(string unlocalizedName) =>
        Items.Values.SingleOrDefault(x => x.UnlocalizedName.EqualsIgnoreCase(unlocalizedName));

    public static ItemStack GetSingleItem(Material mat, ItemMeta? meta = null) => new(mat, 1, meta);

    public static ItemStack GetSingleItem(string unlocalizedName, ItemMeta? meta = null) => new(Get(unlocalizedName).Type, 1, meta);

}
