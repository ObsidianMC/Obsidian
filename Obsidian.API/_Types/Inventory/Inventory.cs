using System;
using System.Collections.Generic;
using System.Linq;

namespace Obsidian.API
{
    public class Inventory
    {
        internal byte Id { get; init; }

        internal int StateId { get; set; }

        public List<IPlayer> Viewers { get; private set; } = new List<IPlayer>();

        public Vector? BlockPosition { get; internal set; }

        public Guid Uuid { get; private set; } = Guid.NewGuid();

        public Guid Owner { get; set; }

        public ChatMessage Title { get; set; } = ChatMessage.Empty;

        public InventoryType Type { get; }

        public int Size { get; }

        public bool IsPlayerInventory => this.Id == 0;

        public ItemStack?[] Items { get; }

        public Inventory(InventoryType type)
        {
            this.Type = type;

            switch (type)
            {
                case InventoryType.Beacon:
                case InventoryType.Lectern:
                    this.Size = 1;
                    break;
                case InventoryType.Grindstone:
                case InventoryType.CartographyTable:
                case InventoryType.Anvil:
                case InventoryType.BlastFurnace:
                case InventoryType.Smoker:
                case InventoryType.Furnace:
                case InventoryType.Merchant:
                    this.Size = 3;
                    break;
                case InventoryType.BrewingStand:
                case InventoryType.Hopper:
                    this.Size = 5;
                    break;
                case InventoryType.Crafting:
                    this.Size = 2 * 5;
                    break;
                case InventoryType.Stonecutter:
                case InventoryType.Enchantment:
                    this.Size = 2;
                    break;
                case InventoryType.Loom:
                    this.Size = 4;
                    break;
                case InventoryType.Generic:
                    this.Size = 9 * 3;
                    break;
                default:
                    break;
            }

            this.Items = new ItemStack[this.Size];
        }

        public Inventory(int size)
        {
            this.Type = InventoryType.Generic;

            if (size % 9 != 0)
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

                    if (invItem?.Type == item.Type)
                    {
                        if (invItem.Count >= 64)
                            continue;

                        invItem.Count += item.Count;

                        return i;
                    }

                    if (invItem == null)
                    {
                        this.Items[i] = item;
                        return i;
                    }
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

                    if (invItem?.Type == item.Type)
                    {
                        if (invItem.Count >= 64)
                            continue;

                        invItem.Count += item.Count;

                        return i;
                    }

                    if (invItem == null)
                        this.Items[i] = item;

                    return i;
                }
            }

            return 9;
        }

        public void SetItem(int slot, ItemStack? item)
        {
            if (slot > this.Size - 1 || slot < 0)
                throw new IndexOutOfRangeException($"{slot} > {this.Size - 1}");

            this.Items[slot] = item;
        }

        public ItemStack? GetItem(int slot) =>
            slot > this.Size - 1 || slot < 0 ? throw new IndexOutOfRangeException(nameof(slot)) : this.Items[slot];

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

        public bool TryRemoveItem(int slot, out ItemStack? removedItem)
        {
            if (slot > this.Size - 1 || slot < 0)
                throw new IndexOutOfRangeException($"{slot} > {this.Size - 1}");

            var item = this.Items[slot];

            if (item == null)
            {
                removedItem = null;
                return false;
            }

            removedItem = item;

            return this.RemoveItem(slot);
        }

        public bool TryRemoveItem(int slot, short amount, out ItemStack? removedItem)
        {
            if (slot > this.Size - 1 || slot < 0)
                throw new IndexOutOfRangeException($"{slot} > {this.Size - 1}");

            var item = this.Items[slot];

            if (item == null)
            {
                removedItem = null;
                return false;
            }

            removedItem = item;

            return this.RemoveItem(slot, amount);
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
