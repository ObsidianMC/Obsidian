using System.Collections.ObjectModel;

namespace Obsidian.API;

public class ItemMetaBuilder
{
    internal int CustomModelData { get; set; }
    public ChatMessage Name { get; internal set; }

    public int Durability { get; internal set; }

    public bool Unbreakable { get; internal set; }

    public IReadOnlyDictionary<EnchantmentType, Enchantment> Enchantments { get; }
    public IReadOnlyDictionary<EnchantmentType, Enchantment> StoredEnchantments { get; }

    public IReadOnlyList<string> CanDestroy { get; }

    public IReadOnlyList<ChatMessage> Lore { get; }

    private readonly Dictionary<EnchantmentType, Enchantment> enchantments = new Dictionary<EnchantmentType, Enchantment>();
    private readonly Dictionary<EnchantmentType, Enchantment> storedEnchantments = new Dictionary<EnchantmentType, Enchantment>();

    private readonly List<string> canDestroy = new List<string>();

    private readonly List<ChatMessage> lore = new List<ChatMessage>();

    public ItemMetaBuilder()
    {
        Enchantments = new ReadOnlyDictionary<EnchantmentType, Enchantment>(enchantments);
        StoredEnchantments = new ReadOnlyDictionary<EnchantmentType, Enchantment>(storedEnchantments);
        CanDestroy = new ReadOnlyCollection<string>(canDestroy);
        Lore = new ReadOnlyCollection<ChatMessage>(lore);
    }

    internal ItemMetaBuilder WithCustomModelData(int modelData)
    {
        CustomModelData = modelData;

        return this;
    }

    public ItemMetaBuilder CouldDestroy(string id)
    {
        canDestroy.Add(id);

        return this;
    }

    public ItemMetaBuilder WithDurability(int durability)
    {
        Durability = durability;

        return this;
    }

    public ItemMetaBuilder WithName(string name)
    {
        Name = ChatMessage.Simple(name);

        return this;
    }

    public ItemMetaBuilder WithName(ChatMessage name)
    {
        Name = name;

        return this;
    }

    public ItemMetaBuilder AddLore(string lore)
    {
        this.lore.Add(ChatMessage.Simple(lore));

        return this;
    }

    public ItemMetaBuilder AddLore(ChatMessage lore)
    {
        this.lore.Add(lore);

        return this;
    }

    public ItemMetaBuilder IsUnbreakable(bool unbreakable)
    {
        Unbreakable = unbreakable;

        return this;
    }

    public ItemMetaBuilder AddEnchantment(EnchantmentType type, int level)
    {
        enchantments.Add(type, new Enchantment
        {
            Type = type,
            Level = level
        });

        return this;
    }

    public ItemMetaBuilder AddStoredEnchantment(EnchantmentType type, int level)
    {
        storedEnchantments.Add(type, new Enchantment
        {
            Type = type,
            Level = level
        });

        return this;
    }

    public ItemMeta Build()
    {
        var meta = new ItemMeta
        {
            CustomModelData = CustomModelData,
            Name = Name,
            Lore = Lore,
            Durability = Durability,
            Unbreakable = Unbreakable,

            Enchantments = new ReadOnlyDictionary<EnchantmentType, Enchantment>(new Dictionary<EnchantmentType, Enchantment>(enchantments)),
            StoredEnchantments = new ReadOnlyDictionary<EnchantmentType, Enchantment>(new Dictionary<EnchantmentType, Enchantment>(storedEnchantments)),

            CanDestroy = new ReadOnlyCollection<string>(new List<string>(canDestroy))
        };

        return meta;
    }
}
