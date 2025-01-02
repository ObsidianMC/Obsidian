using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.API.Utilities;

namespace Obsidian.API.Registries;
public static partial class ItemsRegistry
{
    public static Item Get(INetStreamReader reader) => Get(reader.ReadVarInt());
    public static Item Get(int id) => Items.Values.SingleOrDefault(x => x.Id == id);
    public static Item Get(Material mat) => Items.GetValueOrDefault(mat);
    public static Item Get(string unlocalizedName) =>
        Items.Values.SingleOrDefault(x => x.UnlocalizedName.EqualsIgnoreCase(unlocalizedName));

    public static bool TryGet(Material mat, out Item item) => Items.TryGetValue(mat, out item);

    public static ItemStack Get(string unlocalizedName, short count, params List<IDataComponent> components) => new(Get(unlocalizedName), count, components);

    public static ItemStack GetSingleItem(Material mat, params List<IDataComponent> components) => new(Get(mat), 1, components);

    public static ItemStack GetSingleItem(string unlocalizedName, params List<IDataComponent> components) => new(Get(unlocalizedName), 1, components);

    public static Ingredient GetIngredientFromTag(string tag, short count)
    {
        var ingredient = new Ingredient();

        var tagType = TagsRegistry.Item.All.FirstOrDefault(x => x.Name.EqualsIgnoreCase(tag.Replace("minecraft:", "")));
        foreach (var id in tagType!.Entries)
        {
            var item = Get(id);

            ingredient.Add(new ItemStack(item, count));
        }

        return ingredient;
    }

    public static Ingredient GetIngredientFromName(string name, short count) =>
    [
        Get(name, count)
    ];
}
