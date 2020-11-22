using System.Collections.Generic;

namespace Obsidian.Items
{
    public struct ItemMeta
    {
        internal byte Slot { get; set; }

        internal int CustomModelData { get; set; }

        internal short Count { get; set; }

        public string Name { get; internal set; }

        public string Description { get; internal set; }

        public int RepairAmount { get; internal set; }

        public int Durability { get; internal set; }

        public bool Unbreakable { get; internal set; }

        public IReadOnlyDictionary<EnchantmentType, Enchantment> Enchantments { get; internal set; }
        public IReadOnlyDictionary<EnchantmentType, Enchantment> StoredEnchantments { get; internal set; }

        public IReadOnlyList<string> CanDestroy { get; internal set; }
    }
}
