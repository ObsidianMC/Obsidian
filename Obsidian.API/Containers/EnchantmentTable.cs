using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Obsidian.API.Containers
{
    public sealed class EnchantmentTable : IContainer, ITileEntity
    {
        private ItemStack?[] items;

        public string Id => "enchantment_table";

        public Vector? BlockPosition { get; set; }

        public InventoryType Type => InventoryType.Enchantment;

        public int Size => 2;

        public ChatMessage? Title { get; set; } = "Enchantment Table";

        public EnchantmentTable()
        {
            this.items = new ItemStack?[this.Size];
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

        public IEnumerator<ItemStack> GetEnumerator() => (this.items as IEnumerable<ItemStack>).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
