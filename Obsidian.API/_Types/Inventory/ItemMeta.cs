using System;
using System.Collections.Generic;

namespace Obsidian.API
{
    public struct ItemMeta : IEquatable<ItemMeta>
    {
        internal byte Slot { get; set; }

        internal int CustomModelData { get; set; }

        public IChatMessage Name { get; internal set; }

        public int RepairAmount { get; internal set; }

        public int Durability { get; internal set; }

        public bool Unbreakable { get; internal set; }

        public IReadOnlyDictionary<EnchantmentType, Enchantment> Enchantments { get; internal set; }
        public IReadOnlyDictionary<EnchantmentType, Enchantment> StoredEnchantments { get; internal set; }

        public IReadOnlyList<string> CanDestroy { get; internal set; }

        public IReadOnlyList<IChatMessage> Lore { get; internal set; }

        public bool HasTags() => this.Name != null || this.Lore?.Count > 0 || this.Durability > 0 || this.Unbreakable || this.RepairAmount > 0;

        public bool Equals(ItemMeta other) =>
            (this.Slot, this.CustomModelData, this.Name, this.RepairAmount, this.Durability, this.Unbreakable, this.Enchantments, this.StoredEnchantments, this.CanDestroy, this.Lore) ==
            (other.Slot, other.CustomModelData, other.Name, other.RepairAmount, other.Durability, other.Unbreakable, other.Enchantments, other.StoredEnchantments, other.CanDestroy, other.Lore);

        public override bool Equals(object obj) => obj is ItemMeta meta && Equals(meta);

        public static bool operator ==(ItemMeta left, ItemMeta right) => left.Equals(right);

        public static bool operator !=(ItemMeta left, ItemMeta right) => !(left == right);

        public override int GetHashCode() =>
            (this.Slot, this.CustomModelData, this.Name, this.RepairAmount, this.Durability, this.Unbreakable, this.Enchantments, this.StoredEnchantments, this.CanDestroy, this.Lore).GetHashCode();
    }
}
