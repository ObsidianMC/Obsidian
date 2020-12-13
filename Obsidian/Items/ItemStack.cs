using Obsidian.Blocks;
using Obsidian.Util.Registry;
using System;

namespace Obsidian.Items
{
    public class ItemStack : Item, IEquatable<ItemStack>
    {
        public readonly static ItemStack Air = new ItemStack(0, 0, default);

        internal bool Present { get; set; }

        public short Count { get; internal set; }

        public ItemMeta ItemMeta { get; internal set; }

        public ItemStack() : base("minecraft:air", Materials.Air) { }

        public ItemStack(short itemId, short count, ItemMeta? itemMeta = null) : base(Registry.GetItem(itemId).UnlocalizedName, Registry.GetItem(itemId).Type)
        {
            this.Id = itemId;
            this.Count = count;
            if (count > 0) { Present = true; }

            if (itemMeta != null)
                this.ItemMeta = itemMeta.Value;
        }

        public static ItemStack operator -(ItemStack item, int value)
        {
            if (item.Count <= 0)
                return new ItemStack(0, 0, default);

            item.Count -= (short)value;

            return item;
        }

        public static ItemStack operator +(ItemStack item, int value)
        {
            if (item.Count >= 64)
                return item;

            item.Count += (short)value;

            return item;
        }

        public bool Equals(ItemStack other)
        {
            if (other == null)
                throw new NullReferenceException();

            return this.Type == other.Type && (this.ItemMeta.Name == other.ItemMeta.Name && this.ItemMeta.Lore == other.ItemMeta.Lore);
        }
    }
}
