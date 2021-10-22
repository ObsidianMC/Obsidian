using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Obsidian.API.Containers
{
    public sealed class CartographyTable : IResultContainer
    {
        private ItemStack?[] items;

        public InventoryType Type => InventoryType.CartographyTable;

        public int Size => 3;

        public ChatMessage? Title { get; set; } = "Cartography Table";

        public CartographyTable()
        {
            this.items = new ItemStack?[this.Size];
        }

        public void SetResult(ItemStack? result)
        {

        }

        public ItemStack? GetResult() { return null; }

        public void SetItem(int slot, ItemStack? item)
        {
            if (slot > this.Size - 1 || slot < 0)
                throw new IndexOutOfRangeException($"{slot} > {this.Size - 1}");

            this.items[slot] = item;
        }

        public bool HasItems() => this.items.Any(x => x is not null);

        public IEnumerator<ItemStack> GetEnumerator() => (this.items as IEnumerable<ItemStack>).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
