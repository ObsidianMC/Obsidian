using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Obsidian.API.Containers
{
    public sealed class SmeltingContainer : ISmeltingContainer, ITileEntity
    {
        private ItemStack?[] items;

        public InventoryType Type { get; }

        public int Size => 3;

        public ChatMessage? Title { get; set; }

        public short BurnTime { get; set; }

        public short CookTime { get; set; }

        public short CookTimeTotal { get; set; }

        public string Id { get; }

        public Vector? BlockPosition { get; set; }

        public SmeltingContainer(InventoryType type, string id)
        {
            if (type != InventoryType.Furnace || type != InventoryType.Smoker || type != InventoryType.BlastFurnace || type != InventoryType.Custom)
                throw new InvalidOperationException("Type must either be custom, furnace, blast furnace or smoker.");

            this.items = new ItemStack?[this.Size];
            this.Id = id;
        }

        public ItemStack? GetResult()
        {

            return null;
        }

        public bool HasItems() => this.items.Any(x => x is not null);
        public void SetItem(int slot, ItemStack? item)
        {
            if (slot > this.Size - 1 || slot < 0)
                throw new IndexOutOfRangeException($"{slot} > {this.Size - 1}");

            this.items[slot] = item;
        }

        public void SetResult(ItemStack? result) => throw new NotImplementedException();
        public ItemStack? GetFuel() => throw new NotImplementedException();
        public ItemStack? GetIngredient() => throw new NotImplementedException();

        public IEnumerator<ItemStack> GetEnumerator() => (this.items as IEnumerable<ItemStack>).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public void ToNbt() => throw new NotImplementedException();
        public void FromNbt() => throw new NotImplementedException();
    }
}
