using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Obsidian.API.Containers
{
    public sealed class Loom : IResultContainer
    {
        private ItemStack?[] items;

        public InventoryType Type => InventoryType.Loom;

        public int Size => 4;

        public ChatMessage? Title { get; set; } = "Loom";

        public Loom()
        {
            this.items = new ItemStack?[this.Size];
        }

      
        public ItemStack? GetResult() => throw new NotImplementedException();
        public void SetResult(ItemStack? result) => throw new NotImplementedException();

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
