using System.Collections;
using System.Collections.Generic;

namespace Obsidian.API
{
    public interface IContainer : IEnumerable<ItemStack>, IEnumerable
    {
        public InventoryType Type { get; }

        public int Size { get; }

        public ChatMessage? Title { get; set; }

        /// <summary>
        /// Sets the slot with the specified item.
        /// </summary>
        /// <param name="slot">The slot you want to set.</param>
        /// <param name="item">The item you want to put in that slot. (Can be null for air)</param>
        public void SetItem(int slot, ItemStack? item);

        /// <summary>
        /// Checks if the container has any items inside.
        /// </summary>
        /// <returns>True if there's atleast 1 item in the container.</returns>
        public bool HasItems();
    }

    public interface IResultContainer : IContainer
    {
        public void SetResult(ItemStack? result);

        public ItemStack? GetResult();
    }

    public interface ISmeltingContainer : IResultContainer
    {
        public short BurnTime { get; set; }

        public short CookTime { get; set; }

        public short CookTimeTotal { get; set; }

        public ItemStack? GetFuel();

        public ItemStack? GetIngredient();
    }
}
