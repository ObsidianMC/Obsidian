using Obsidian.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Obsidian.Items
{
    public class Inventory
    {
        internal int Id { get; set; }

        internal int ActionsNumber { get; set; }
        
        public InventoryType Type { get; set; }

        public string Title { get; private set; }

        public int Size { get; private set; } = 9 * 3;

        public Dictionary<int, ItemStack> Items { get; private set; } = new Dictionary<int, ItemStack>();

        public List<Player> Viewers { get; private set; } = new List<Player>();

        public void AddItem(ItemStack item)
        {
            int lastIndex = this.Items.Keys.OrderByDescending(x => x).FirstOrDefault();
            lastIndex = lastIndex == 0 ? 0 : lastIndex + 1;

            this.Items.Add(lastIndex, item);
        }

        public void SetItem(int slot, ItemStack item)
        {
            if (slot > this.Size - 1 || slot < 0)
                throw new IndexOutOfRangeException(nameof(slot));

            this.Items.Add(slot, item);
        }

        public ItemStack GetItem(int slot)
        {
            if (slot > this.Size - 1 || slot < 0)
                throw new IndexOutOfRangeException(nameof(slot));

            if (!this.Items.ContainsKey(slot))
                throw new KeyNotFoundException(nameof(slot));

            return this.Items.GetValueOrDefault(slot);
        }

        public bool RemoveItem(int slot)
        {
            if (slot > this.Size - 1 || slot < 0)
                throw new IndexOutOfRangeException(nameof(slot));

            return this.Items.Remove(slot);
        }
    }

    public enum InventoryType
    {
        Generic,
        Anvil,
        Beacon,
        BlastFurnace,
        BrewingStand,
        Crafting,
        Enchantment,
        Furnace,
        Grindstone,
        Hopper,
        Lectern,
        Loom,
        Merchant,
        ShulkerBox,
        Smoker,
        CartographyTable,
        Stonecutter
    }
}
