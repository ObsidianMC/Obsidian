using System.Collections.Generic;

namespace Obsidian.Items
{
    public sealed class ItemMeta
    {
        internal byte Slot { get; set; } = 0;

        internal int CustomModelData { get; set; } = 0;

        internal short Count { get; set; } = 0;

        public string Name { get; internal set; }

        public string Description { get; internal set; }

        public int RepairAmount { get; internal set; } = 0;

        public int Durability { get; internal set; } = 0;

        public bool Unbreakable { get; internal set; } = false;

        public IReadOnlyDictionary<EnchantmentType, Enchantment> Enchantments { get; internal set; }
        public IReadOnlyDictionary<EnchantmentType, Enchantment> StoredEnchantments { get; internal set; }

        public IReadOnlyList<string> CanDestroy { get; internal set; }
    }
}
