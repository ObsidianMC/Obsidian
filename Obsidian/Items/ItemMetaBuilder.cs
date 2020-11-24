using Obsidian.Util.Extensions;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Obsidian.Items
{
    public class ItemMetaBuilder
    {
        internal byte Slot { get; set; }

        internal int CustomModelData { get; set; }
        public string Name { get; internal set; }

        public string Description { get; internal set; }


        public int Durability { get; internal set; }

        public bool Unbreakable { get; internal set; }

        public IReadOnlyDictionary<EnchantmentType, Enchantment> Enchantments { get; }
        public IReadOnlyDictionary<EnchantmentType, Enchantment> StoredEnchantments { get; }

        public IReadOnlyList<string> CanDestroy { get; }

        private readonly Dictionary<EnchantmentType, Enchantment> enchantments = new Dictionary<EnchantmentType, Enchantment>();
        private readonly Dictionary<EnchantmentType, Enchantment> storedEnchantments = new Dictionary<EnchantmentType, Enchantment>();
        private readonly List<string> canDestroy = new List<string>();

        public ItemMetaBuilder()
        {
            this.Enchantments = new ReadOnlyDictionary<EnchantmentType, Enchantment>(this.enchantments);
            this.StoredEnchantments = new ReadOnlyDictionary<EnchantmentType, Enchantment>(this.storedEnchantments);

            this.CanDestroy = new ReadOnlyCollection<string>(this.canDestroy);
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

        public ItemMetaBuilder WithDurability(int durability)
        {
            this.Durability = durability;

            return this;
        }

        public ItemMetaBuilder WithName(string name)
        {
            this.Name = name;

            return this;
        }

        public ItemMetaBuilder WithDescription(string description)
        {
            this.Description = description;

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
                Id = type.ToString().ToSnakeCase(),
                Level = level
            });

            return this;
        }

        public ItemMetaBuilder AddStoredEnchantment(EnchantmentType type, int level)
        {
            this.storedEnchantments.Add(type, new Enchantment
            {
                Id = type.ToString().ToSnakeCase(),
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
                Lore = this.Description,
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
