using Obsidian.Blocks;
using System;

namespace Obsidian.Items
{
    public class ItemStack : Item, IEquatable<ItemStack>
    {
        public readonly static ItemStack Air = new ItemStack(0, 0);

        internal bool Present { get; set; }

        public short Count { get; internal set; }

        public ItemMeta ItemMeta { get; internal set; }

        public ItemStack() : base(0, "minecraft:air", Materials.Air) { }

        public ItemStack(short itemId, short count, ItemMeta? meta = null) : base(itemId, Registry.GetItem(itemId).UnlocalizedName, Registry.GetItem(itemId).Type)
        {
            this.Count = count;
            if (count > 0) { Present = true; }

            if(meta.HasValue)
                this.ItemMeta = meta.Value;
        }

        public static ItemStack operator -(ItemStack item, int value)
        {
            if (item.Count <= 0)
                return new ItemStack(0, 0);

            item.Count -= (short)value;

            return item;
        }

        public static bool operator ==(ItemStack left, ItemStack right) => left.Equals(right);

        public static bool operator !=(ItemStack left, ItemStack right) => !(left == right);

        public static ItemStack operator +(ItemStack item, int value)
        {
            if (item.Count >= 64)
                return item;

            item.Count += (short)value;

            return item;
        }

        public bool Equals(ItemStack other) => (this.Type, this.ItemMeta) == (other.Type, other.ItemMeta);

        public override bool Equals(object obj) => obj is ItemStack itemStack && Equals(itemStack);

        public override int GetHashCode() =>
            (this.Present, this.Count, this.ItemMeta, this.Id, this.UnlocalizedName).GetHashCode();
    }
}
