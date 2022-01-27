using Microsoft.Extensions.Logging;
using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;
using System.Text.Json;

namespace Obsidian.Net.Packets.Play.Serverbound;

// Source: https://wiki.vg/index.php?title=Protocol&oldid=14889#Click_Window
public partial class ClickWindowPacket : IServerboundPacket
{
    private const int Outsideinventory = -999;

    /// <summary>
    /// The ID of the window which was clicked. 0 for player inventory.
    /// </summary>
    [Field(0)]
    public byte WindowId { get; private set; }


    /// <summary>
    /// The last recieved State ID from either a Set Slot or a Window Items packet
    /// </summary>
    [Field(1), VarLength]
    public int StateId { get; private set; }

    /// <summary>
    /// The clicked slot number
    /// </summary>
    [Field(2)]
    public short ClickedSlot { get; private set; }

    /// <summary>
    /// The button used in the click
    /// </summary>
    [Field(3)]
    public sbyte Button { get; private set; }

    /// <summary>
    /// Inventory operation mode
    /// </summary>
    [Field(4), ActualType(typeof(int)), VarLength]
    public InventoryOperationMode Mode { get; private set; }

    [Field(5)]
    public IDictionary<short, ItemStack> SlotsUpdated { get; private set; }

    /// <summary>
    /// The clicked slot. Has to be empty (item ID = -1) for drop mode.
    /// </summary>
    [Field(6)]
    public ItemStack ClickedItem { get; private set; }

    private bool IsPlayerInventory => this.WindowId == 0;

    public int Id => 0x08;

    public void Populate(byte[] data) => throw new NotImplementedException();

    public void Populate(MinecraftStream stream) => throw new NotImplementedException();

    public async ValueTask HandleAsync(Server server, Player player)
    {
        var container = player.OpenedContainer ?? player.Inventory;

        var (slot, forPlayer) = container.GetDifference(ClickedSlot);

        if (this.IsPlayerInventory || forPlayer)
            container = player.Inventory;

        switch (Mode)
        {
            case InventoryOperationMode.MouseClick:
                await HandleMouseClick(container, server, player, slot);
                break;

            case InventoryOperationMode.ShiftMouseClick:
                {
                    if (ClickedItem == null)
                        return;

                    //TODO implement shift click

                    break;
                }

            case InventoryOperationMode.NumberKeys:
                {
                    var localSlot = Button + 36;

                    var currentItem = player.Inventory.GetItem(localSlot);

                    if (currentItem.IsAir() && ClickedItem != null)
                    {
                        container.RemoveItem(slot);

                        player.Inventory.SetItem(localSlot, ClickedItem);
                    }
                    else if (!currentItem.IsAir() && ClickedItem != null)
                    {
                        container.SetItem(slot, currentItem);

                        player.Inventory.SetItem(localSlot, ClickedItem);
                    }
                    else
                    {
                        container.SetItem(slot, currentItem);

                        player.Inventory.RemoveItem(localSlot);
                    }

                    break;
                }

            case InventoryOperationMode.MiddleMouseClick:
                break;

            case InventoryOperationMode.Drop:
                {
                    if (ClickedSlot != Outsideinventory)
                    {
                        ItemStack? removedItem;
                        if (Button == 0)
                            container.RemoveItem(slot, 1, out removedItem);
                        else
                            container.RemoveItem(slot, 64, out removedItem);

                        if (removedItem == null)
                            return;

                        var loc = new VectorF(player.Position.X, (float)player.HeadY - 0.3f, player.Position.Z);

                        var item = new ItemEntity
                        {
                            EntityId = player + player.World.GetTotalLoadedEntities() + 1,
                            Count = 1,
                            Id = removedItem.AsItem().Id,
                            Glowing = true,
                            World = player.World,
                            Position = loc
                        };

                        var lookDir = player.GetLookDirection();
                        var vel = Velocity.FromDirection(loc, lookDir);

                        //TODO Get this shooting out from the player properly.
                        server.BroadcastPacket(new SpawnEntityPacket
                        {
                            EntityId = item.EntityId,
                            Uuid = item.Uuid,
                            Type = EntityType.Item,
                            Position = item.Position,
                            Pitch = 0,
                            Yaw = 0,
                            Data = 1,
                            Velocity = vel
                        });
                        server.BroadcastPacket(new EntityMetadata
                        {
                            EntityId = item.EntityId,
                            Entity = item
                        });
                    }
                    break;
                }
            case InventoryOperationMode.MouseDrag:
                HandleDragClick(container, player, slot);
                break;

            case InventoryOperationMode.DoubleClick:
                {
                    if (ClickedItem == null || ClickedItem.Count >= 64)
                        return;

                    TakeFromContainer(container, player.Inventory);
                    break;
                }
        }

        if (container is IBlockEntity tileEntityContainer)
        {
            var blockEntity = await player.World.GetBlockEntityAsync(tileEntityContainer.BlockPosition);

            if (blockEntity is null)
                return;

            if (blockEntity.TryGetTag("Items", out var list))
            {
                var items = list as NbtList;

                var itemsToBeRemoved = new HashSet<int>();
                var itemsToBeUpdated = new HashSet<NbtCompound>();

                items!.Clear();

                this.FillNbtList(items, container);
            }
            else
            {
                var items = new NbtList(NbtTagType.Compound, "Items");

                this.FillNbtList(items, container);

                blockEntity.Add(items);
            }
        }
    }

    private void TakeFromContainer(BaseContainer container, BaseContainer playerContainer)
    {
        int amountNeeded = 64 - ClickedItem.Count; // TODO use max item count
        if (amountNeeded == 0)
            return;

        for (int i = 0; i < container.Size; i++)
        {
            ItemStack? item = container[i];
            if (item is null || item != ClickedItem)
                continue;

            int amountTaken = Math.Min(item.Count, amountNeeded);
            item.Count -= amountTaken;

            if (item.Count == 0)
                container.RemoveItem(i);

            ClickedItem.Count += amountTaken;
            amountNeeded -= amountTaken;

            if (amountNeeded == 0)
                break;
        }

        //Try the player inventory
        if (amountNeeded > 0 && !this.IsPlayerInventory)
        {
            for (int i = 0; i < playerContainer.Size; i++)
            {
                ItemStack? item = playerContainer[i];
                if (item is null || item != ClickedItem)
                    continue;

                int amountTaken = Math.Min(item.Count, amountNeeded);
                item.Count -= amountTaken;

                if (item.Count == 0)
                    playerContainer.RemoveItem(i);

                ClickedItem.Count += amountTaken;
                amountNeeded -= amountTaken;

                if (amountNeeded == 0)
                    break;
            }
        }
    }

    private void FillNbtList(NbtList items, BaseContainer container)
    {
        for (int i = 0; i < container.Size; i++)
        {
            var item = container[i];

            if (item is null)
                continue;

            item.Slot = i;

            items.Add(item.ToNbt());
        }
    }

    private async Task HandleMouseClick(BaseContainer container, Server server, Player player, int slot)
    {
        if (!ClickedItem.IsAir())
        {
            var @event = await server.Events.InvokeContainerClickAsync(new ContainerClickEventArgs(player, container, ClickedItem)
            {
                Slot = slot
            });

            if (@event.Cancel)
                return;

            player.LastClickedItem = ClickedItem;

            container.RemoveItem(slot);
        }
        else
        {
            if (Button == 0)
            {
                server.Logger.LogDebug("Placed: {} in container: {}", player.LastClickedItem?.Type, container.Title?.Text);
                container.SetItem(slot, player.LastClickedItem);

                player.LastClickedItem = ClickedItem;
            }
            else
            {
                container.SetItem(slot, player.LastClickedItem);

                player.LastClickedItem = ClickedItem;
            }
        }
    }

    private void HandleDragClick(BaseContainer container, Player player, int value)
    {
        if (ClickedSlot == Outsideinventory)
        {
            if (Button == 0 || Button == 4 || Button == 8)
                player.isDragging = true;
            else if (Button == 2 || Button == 6 || Button == 10)
                player.isDragging = false;
        }
        else if (player.isDragging)
        {
            if (player.Gamemode == Gamemode.Creative)
            {
                if (Button != 9)
                    return;

                container.SetItem(value, ClickedItem);
            }
            else
            {
                // 1 = left mouse
                // 5 = right mouse
                if (Button != 1 || Button != 5)
                    return;

                container.SetItem(value, ClickedItem);
            }
        }
    }
}

public enum InventoryOperationMode : int
{
    MouseClick,
    ShiftMouseClick,
    NumberKeys,
    MiddleMouseClick,
    Drop,
    MouseDrag,
    DoubleClick
}
