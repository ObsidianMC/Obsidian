using System.Collections;
using System.Collections.Generic;

namespace Obsidian.API
{
    public interface IContainer : IEnumerable<ItemStack>, IEnumerable
    {
        public InventoryType Type { get; }

        public int Size { get; }

        public ChatMessage Title { get; set; }

        public void SetItem(int slot, ItemStack? item);

        public bool HasItems();
    }
}
