using Obsidian.API.Crafting;
using Obsidian.API.Utilities;

namespace Obsidian.API.Registries;
public static partial class ItemsRegistry
{
    public static Item Get(int id) => Items.Values.SingleOrDefault(x => x.Id == id);
    public static Item Get(Material mat) => Items.GetValueOrDefault(mat);
    public static Item Get(string unlocalizedName) =>
        Items.Values.SingleOrDefault(x => x.UnlocalizedName.EqualsIgnoreCase(unlocalizedName));

    public static bool TryGet(Material mat, out Item item) => Items.TryGetValue(mat, out item);

    public static ItemStack Get(string unlocalizedName, short count, ItemMeta? meta = null) => new(Get(unlocalizedName).Type, count, meta);

    public static ItemStack GetSingleItem(Material mat, ItemMeta? meta = null) => new(mat, 1, meta);

    public static ItemStack GetSingleItem(string unlocalizedName, ItemMeta? meta = null) => new(Get(unlocalizedName).Type, 1, meta);

    public static Ingredient GetIngredientFromTag(string tag, short count)
    {
        var ingredient = new Ingredient();

        var tagType = TagsRegistry.Item.All.FirstOrDefault(x => x.Name.EqualsIgnoreCase(tag.Replace("minecraft:", "")));
        foreach (var id in tagType!.Entries)
        {
            var item = Get(id);

            ingredient.Add(new ItemStack(item.Type, count));
        }

        return ingredient;
    }

    public static Ingredient GetIngredientFromName(string name, short count) =>
    [
        Get(name, count)
    ];
}
