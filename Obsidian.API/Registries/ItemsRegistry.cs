using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.API.Plugins;
using Obsidian.API.Utilities;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Obsidian.API.Registries;
/// <summary>
/// Every item: vanilla's, as static properties and in vanilla's id order, then the ones plugins registered.
/// </summary>
/// <remarks>
/// Like vanilla's item registry, looking up an unknown id or identifier gives <see cref="Air"/>; use
/// <see cref="TryGet(string, out Item)"/> to tell the two apart. Ids only grow while the server runs: an item a plugin
/// registered keeps its id when the plugin reloads or unloads.
/// </remarks>
public static partial class ItemsRegistry
{
    private static readonly Lock registering = new();

    // Items by id and identifier. Registering replaces the table whole, so lookups never lock.
    private static ItemTable table;

    // Builds the table once the generated static properties exist: field initializers across partial declarations run
    // in no particular order, but all before the static constructor.
    static ItemsRegistry()
    {
        var vanilla = Items.Values.OrderBy(item => item.Id).ToArray();

        VanillaCount = vanilla.Length;
        table = new ItemTable(vanilla);
    }

    /// <summary>
    /// How many items vanilla has; ids from this one on are plugins' items.
    /// </summary>
    public static int VanillaCount { get; }

    /// <summary>
    /// Every item, by id.
    /// </summary>
    public static IReadOnlyList<Item> All => table.ById;

    /// <summary>
    /// Whether a plugin registered any item, which clients without that plugin can't show.
    /// </summary>
    public static bool HasPluginItems => table.ById.Length > VanillaCount;

    public static Item Get(INetStreamReader reader) => Get(reader.ReadVarInt());

    public static Item Get(int id)
    {
        var items = table.ById;

        return (uint)id < (uint)items.Length ? items[id] : Air;
    }

    /// <summary>
    /// Finds an item by its id.
    /// </summary>
    public static bool TryGet(int id, [NotNullWhen(true)] out Item? item)
    {
        var items = table.ById;
        item = (uint)id < (uint)items.Length ? items[id] : null;

        return item is not null;
    }

    public static Item Get(Material mat) => Items.GetValueOrDefault(mat) ?? Air;

    public static Item Get(string unlocalizedName) => TryGet(unlocalizedName, out var item) ? item : Air;

    /// <summary>
    /// Finds an item by its identifier, ignoring case.
    /// </summary>
    public static bool TryGet(string unlocalizedName, [NotNullWhen(true)] out Item? item) =>
        table.ByName.TryGetValue(unlocalizedName, out item);

    /// <summary>
    /// Registers an item, or returns the one registered before under the same identifier, which keeps its id.
    /// </summary>
    /// <exception cref="ArgumentException">The identifier is one of vanilla's.</exception>
    internal static Item Register(string unlocalizedName)
    {
        lock (registering)
        {
            if (table.ByName.TryGetValue(unlocalizedName, out var existing))
            {
                if (existing.Id < VanillaCount)
                    throw new ArgumentException($"{unlocalizedName} is a vanilla item.", nameof(unlocalizedName));

                return existing;
            }

            var item = new Item(table.ById.Length, unlocalizedName, type: null);
            table = new ItemTable([.. table.ById, item]);

            return item;
        }
    }

    /// <summary>
    /// Drops the behaviour of every item <paramref name="plugin"/> registered, as the plugin unloads. The items stay
    /// registered, with their ids, for stacks of them and for the plugin to register again when it comes back.
    /// </summary>
    internal static void ReleasePlugin(IPluginContainer plugin)
    {
        foreach (var item in table.ById.AsSpan(VanillaCount))
        {
            if (item.Binding?.Owner == plugin)
                item.Binding = null;
        }
    }

    private sealed class ItemTable(Item[] byId)
    {
        public Item[] ById { get; } = byId;

        public FrozenDictionary<string, Item> ByName { get; } =
            byId.ToFrozenDictionary(item => item.UnlocalizedName, StringComparer.OrdinalIgnoreCase);
    }

    public static bool TryGet(Material mat, [NotNullWhen(true)] out Item? item) => Items.TryGetValue(mat, out item);

    public static ItemStack Get(string unlocalizedName, short count, params List<DataComponent> components) =>
        new(Get(unlocalizedName), count, components);

    public static ItemStack GetSingleItem(Material mat, params List<DataComponent> components) => new(Get(mat), 1, components);

    public static ItemStack GetSingleItem(string unlocalizedName, params List<DataComponent> components) => new(Get(unlocalizedName), 1, components);

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
