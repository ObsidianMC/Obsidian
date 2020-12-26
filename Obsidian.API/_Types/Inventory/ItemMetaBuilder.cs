using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Obsidian.API
{
    public class ItemMetaBuilder
    {
        internal byte Slot { get; set; }

        internal int CustomModelData { get; set; }
        public IChatMessage Name { get; internal set; }

        public int Durability { get; internal set; }

        public bool Unbreakable { get; internal set; }

        public IReadOnlyDictionary<EnchantmentType, Enchantment> Enchantments { get; }
        public IReadOnlyDictionary<EnchantmentType, Enchantment> StoredEnchantments { get; }

        public IReadOnlyList<string> CanDestroy { get; }

        public IReadOnlyList<IChatMessage> Lore { get; }

        private readonly Dictionary<EnchantmentType, Enchantment> enchantments = new Dictionary<EnchantmentType, Enchantment>();
        private readonly Dictionary<EnchantmentType, Enchantment> storedEnchantments = new Dictionary<EnchantmentType, Enchantment>();

        private readonly List<string> canDestroy = new List<string>();

        private readonly List<IChatMessage> lore = new List<IChatMessage>();

        public ItemMetaBuilder()
        {
            this.Enchantments = new ReadOnlyDictionary<EnchantmentType, Enchantment>(this.enchantments);
            this.StoredEnchantments = new ReadOnlyDictionary<EnchantmentType, Enchantment>(this.storedEnchantments);

            this.CanDestroy = new ReadOnlyCollection<string>(this.canDestroy);

            this.Lore = new ReadOnlyCollection<IChatMessage>(this.lore);
        }

        internal ItemMetaBuilder WithSlot(byte slot)
        {
            this.Slot = slot;

            return this;
        }

        internal ItemMetaBuilder WithCustomModelData(int modelData)
        {
            this.CustomModelData = modelData;

            return this;
        }

        public ItemMetaBuilder CouldDestroy(string id)
        {
            this.canDestroy.Add(id);

            return this;
        }

        public ItemMetaBuilder WithDurability(int durability)
        {
            this.Durability = durability;

            return this;
        }

        public ItemMetaBuilder WithName(string name)
        {
            this.Name = IChatMessage.Simple(name);

            return this;
        }

        public ItemMetaBuilder AddLore(string lore)
        {
            this.lore.Add(IChatMessage.Simple(lore));

            return this;
        }

        public ItemMetaBuilder AddLore(IChatMessage lore)
        {
            this.lore.Add(lore);

            return this;
        }

        public ItemMetaBuilder IsUnbreakable(bool unbreakable)
        {
            this.Unbreakable = unbreakable;

            return this;
        }

        public ItemMetaBuilder AddEnchantment(EnchantmentType type, int level)
        {
            this.enchantments.Add(type, new Enchantment
            {
                Type = type,
                Level = level
            });

            return this;
        }

        public ItemMetaBuilder AddStoredEnchantment(EnchantmentType type, int level)
        {
            this.storedEnchantments.Add(type, new Enchantment
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
                Slot = this.Slot,
                CustomModelData = this.CustomModelData,
                Name = this.Name,
                Lore = this.Lore,
                Durability = this.Durability,
                Unbreakable = this.Unbreakable,

                Enchantments = new ReadOnlyDictionary<EnchantmentType, Enchantment>(new Dictionary<EnchantmentType, Enchantment>(this.enchantments)),
                StoredEnchantments = new ReadOnlyDictionary<EnchantmentType, Enchantment>(new Dictionary<EnchantmentType, Enchantment>(this.storedEnchantments)),

                CanDestroy = new ReadOnlyCollection<string>(new List<string>(this.canDestroy))
            };

            return meta;
        }
    }
}
