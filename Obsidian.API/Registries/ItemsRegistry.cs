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
        var vanilla = vanillaItems.OrderBy(item => item.Id).ToArray();

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
    /// Whether a plugin registered any item, which clients without that plugin's mod can't show. Plugins' items stay
    /// registered until the server stops, even once their plugin unloads, since stacks of them may still be around.
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

            // The protocol allows more, but Obsidian's ids are shorts.
            if (table.ById.Length > short.MaxValue)
                throw new InvalidOperationException("No item ids are left.");

            var item = new Item(table.ById.Length, unlocalizedName);
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
        lock (registering)
        {
            foreach (var item in table.ById.AsSpan(VanillaCount))
            {
                if (item.Binding?.Owner == plugin)
                    item.Binding = null;
            }
        }
    }

    /// <summary>
    /// Gives an item its properties and its plugin's behaviour, unless the plugin started unloading: then its items
    /// were released already, and binding one now would keep the plugin loaded.
    /// </summary>
    /// <param name="closing">Whether the plugin started unloading.</param>
    /// <returns>Whether the item was bound.</returns>
    internal static bool TryBind(Item item, ItemProperties properties, IPluginContainer plugin, Func<bool> closing)
    {
        lock (registering)
        {
            if (closing())
                return false;

            item.MaxStackSize = properties.MaxStackSize;
            item.MaxDamage = properties.MaxDamage;
            item.Enchantable = properties.Enchantable;
            item.Binding = new ItemBinding(plugin, properties.Behavior);

            return true;
        }
    }

    private sealed class ItemTable(Item[] byId)
    {
        public Item[] ById { get; } = byId;

        public FrozenDictionary<string, Item> ByName { get; } =
            byId.ToFrozenDictionary(item => item.UnlocalizedName, StringComparer.OrdinalIgnoreCase);
    }

    public static ItemStack Get(string unlocalizedName, short count, params List<DataComponent> components) =>
        new(Get(unlocalizedName), count, components);

    /// <summary>
    /// A stack of one <paramref name="item"/>.
    /// </summary>
    public static ItemStack GetSingleItem(Item item, params List<DataComponent> components) => new(item, 1, components);

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
