using System;

namespace Obsidian.API
{
    public class ItemStack : IEquatable<ItemStack>
    {
        public readonly static ItemStack Air = new ItemStack(Material.Air, 0);

        internal bool Present { get; set; }

        public short Count { get; internal set; }

        public ItemMeta ItemMeta { get; internal set; }

        public Material Type { get; }

        public ItemStack(Material type, short count = 1, ItemMeta? meta = null)
        {
            this.Type = type;
            this.Count = count;
            if (count > 0) { Present = true; }

            if (meta.HasValue)
                this.ItemMeta = meta.Value;
        }

        public static ItemStack operator -(ItemStack item, int value)
        {
            if (item.Count <= 0)
                return Air;

            item.Count -= (short)value;

            return item;
        }

        public static bool operator ==(ItemStack? left, ItemStack? right)
        {
            if (ReferenceEquals(left, right))
                return true;

            if (left is null || right is null)
                return false;

            return left.Equals(right);
        }

        public static bool operator !=(ItemStack? left, ItemStack? right) => !(left == right);

        public static ItemStack operator +(ItemStack item, int value)
        {
            if (item.Count >= 64)
                return item;

            item.Count += (short)value;

            return item;
        }

        public bool Equals(ItemStack other) => (this.Type, this.ItemMeta) == (other.Type, other.ItemMeta);

        public override bool Equals(object? obj) => obj is ItemStack itemStack && Equals(itemStack);

        public override int GetHashCode() =>
            (this.Present, this.Count, this.ItemMeta).GetHashCode();
    }
}
