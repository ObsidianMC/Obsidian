using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Obsidian.API.Containers
{
    public sealed class AnvilContainer : IResultContainer
    {
        private ItemStack?[] items;

        public string Id { get; }

        public Vector? BlockPosition { get; set; }

        public InventoryType Type => InventoryType.Anvil;

        public int Size => 3;

        public ChatMessage? Title { get; set; }

        public AnvilContainer(string id)
        {
            this.Id = id;
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

        public void SetResult(ItemStack? result) => throw new NotImplementedException();
        public ItemStack? GetResult() => throw new NotImplementedException();
    }
}
