using System;
using System.Collections.Generic;
using System.Linq;

namespace Obsidian.API
{
    public class Inventory
    {
        internal byte Id { get; set; }

        internal int ActionsNumber { get; set; }

        public List<IPlayer> Viewers { get; private set; } = new List<IPlayer>();

        public Position BlockPosition { get; set; }

        public Guid Owner { get; set; }

        public IChatMessage Title { get; set; } = IChatMessage.Empty;

        public InventoryType Type { get; }

        public int Size { get; }

        public bool IsPlayerInventory { get; }

        public ItemStack[] Items { get; }

        public Inventory(InventoryType type, int size = 0, bool isPlayerInventory = false)
        {
            this.Type = type;
            this.IsPlayerInventory = isPlayerInventory;

            if (size <= 0)
            {
                switch (type)
                {
                    case InventoryType.Beacon:
                    case InventoryType.Lectern:
                        size = 1;
                        break;
                    case InventoryType.Grindstone:
                    case InventoryType.CartographyTable:
                    case InventoryType.Anvil:
                    case InventoryType.BlastFurnace:
                    case InventoryType.Smoker:
                    case InventoryType.Furnace:
                    case InventoryType.Merchant:
                        size = 3;
                        break;
                    case InventoryType.BrewingStand:
                    case InventoryType.Hopper:
                        size = 5;
                        break;
                    case InventoryType.Crafting:
                        size = 2 * 5;
                        break;
                    case InventoryType.Stonecutter:
                    case InventoryType.Enchantment:
                        size = 2;
                        break;
                    case InventoryType.Loom:
                        size = 4;
                        break;
                    case InventoryType.Generic:
                        size = 9 * 3;
                        break;
                    default:
                        break;
                }
            }

            if (!this.IsPlayerInventory && type == InventoryType.Generic && size % 9 != 0)
                throw new InvalidOperationException("Size must be divisble by 9");
            if (size > 9 * 6)
                throw new InvalidOperationException($"Size must be <= {9 * 6}");

            this.Size = size;
            this.Items = new ItemStack[size];
        }

        public void AddItems(params ItemStack[] items)
        {
            foreach (var item in items)
            {
                if (item is null)
                    throw new NullReferenceException(nameof(item));

                this.AddItem(item);
            }
        }

        public int AddItem(ItemStack item)
        {
            if (this.IsPlayerInventory)
            {
                for (int i = 36; i < 45; i++)
                {
                    var invItem = this.Items[i];

                    if (invItem == null)
                        continue;

                    if (invItem.Type == item.Type)
                    {
                        if (invItem.Count >= 64)
                            continue;

                        invItem.Count += item.Count;

                        return i;
                    }

                    this.Items[i] = item;

                    return i;
                }

                for (int i = 9; i < 36; i++)
                {
                    var invItem = this.Items[i];

                    if (invItem != null)
                    {
                        if (invItem.Count >= 64)
                            continue;

                        invItem.Count += item.Count;

                        return i;
                    }

                    this.Items[i] = item;

                    return i;
                }
            }
            else
            {
                for (int i = 0; i < this.Size; i++)
                {
                    var invItem = this.Items[i];

                    if (invItem == null)
                        continue;

                    if (invItem.Type == item.Type)
                    {
                        if (invItem.Count >= 64)
                            continue;

                        invItem.Count += item.Count;

                        return i;
                    }

                    this.Items[i] = item;

                    return i;
                }
            }

            return 9;
        }

        public void SetItem(int slot, ItemStack item)
        {
            if (slot > this.Size - 1 || slot < 0)
                throw new IndexOutOfRangeException($"{slot} > {this.Size - 1}");

            this.Items[slot] = item;
        }

        public ItemStack GetItem(int slot)
        {
            if (slot > this.Size - 1 || slot < 0)
                throw new IndexOutOfRangeException(nameof(slot));

            return this.Items[slot] ?? ItemStack.Air;
        }

        public bool RemoveItem(int slot, short amount = 1)
        {
            if (slot > this.Size - 1 || slot < 0)
                throw new IndexOutOfRangeException($"{slot} > {this.Size - 1}");

            var item = this.Items[slot];

            if (item == null)
                return false;

            if (amount >= 64 || item.Count - amount <= 0)
                this.Items[slot] = null;
            else
                item.Count -= amount;

            return true;
        }

        public bool HasItems() => this.Items.Any(x => x is not null);
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
