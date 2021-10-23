using System;
using System.Linq;

namespace Obsidian.API.Containers
{
    public sealed class EnchantmentTable : AbstractContainer, ITileEntity
    {
        public string Id => "enchantment_table";

        public Vector? BlockPosition { get; set; }

        public EnchantmentTable() : base(2, InventoryType.Enchantment)
        {
            this.Title = "Enchantment Table";
        }

        public bool HasItems() => this.items.Any(x => x is not null);
        public void SetItem(int slot, ItemStack? item)
        {
            if (slot > this.Size - 1 || slot < 0)
                throw new IndexOutOfRangeException($"{slot} > {this.Size - 1}");

            this.items[slot] = item;
        }

        public void FromNbt() => throw new NotImplementedException();
        public void ToNbt() => throw new NotImplementedException();
    }
}
