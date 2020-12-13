using Obsidian.Chat;
using System.Collections.Generic;

namespace Obsidian.Items
{
    public struct ItemMeta
    {
        internal byte Slot { get; set; }

        internal int CustomModelData { get; set; }

        public ChatMessage Name { get; internal set; }

        public int RepairAmount { get; internal set; }

        public int Durability { get; internal set; }

        public bool Unbreakable { get; internal set; }

        public IReadOnlyDictionary<EnchantmentType, Enchantment> Enchantments { get; internal set; }
        public IReadOnlyDictionary<EnchantmentType, Enchantment> StoredEnchantments { get; internal set; }

        public IReadOnlyList<string> CanDestroy { get; internal set; }

        public IReadOnlyList<ChatMessage> Lore { get; internal set; }

        public bool HasTags() => this.Name != null || this.Lore?.Count > 0 || this.Durability > 0 || this.Unbreakable || this.RepairAmount > 0;
    }
}
