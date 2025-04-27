using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

// Source: https://wiki.vg/index.php?title=Protocol&oldid=14889#Click_Window
public partial class ContainerClickPacket
{
    private const int Outsideinventory = -999;

    /// <summary>
    /// The ID of the window which was clicked. 0 for player inventory.
    /// </summary>
    [Field(0)]
    public int ContainerId { get; private set; }


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
    public IDictionary<short, ItemStack?> ChangedSlots { get; private set; } = default!;

    /// <summary>
    /// 	Item carried by the cursor. Has to be empty (item ID = -1) for drop mode, otherwise nothing will happen.
    /// </summary>
    [Field(6)]
    public ItemStack? CarriedItem { get; private set; }

    private bool IsPlayerInventory => this.ContainerId == 0;

    public override void Populate(INetStreamReader reader)
    {
        this.ContainerId = reader.ReadVarInt();
        this.StateId = reader.ReadVarInt();
        this.ClickedSlot = reader.ReadShort();
        this.Button = reader.ReadSignedByte();
        this.Mode = reader.ReadVarInt<InventoryOperationMode>();
        
        var length = reader.ReadVarInt();

        this.ChangedSlots = new Dictionary<short, ItemStack?>(length);
        for (int i = 0; i < length; i++)
            this.ChangedSlots.Add(reader.ReadShort(), reader.ReadItemStack());

        this.CarriedItem = reader.ReadItemStack();
    }

    public async override ValueTask HandleAsync(IServer server, IPlayer player)
    {
        var container = player.OpenedContainer ?? player.Inventory;

        var (slot, forPlayer) = container.GetDifference(ClickedSlot);

        if (this.IsPlayerInventory || forPlayer)
            container = player.Inventory;

        switch (Mode)
        {
            case InventoryOperationMode.MouseClick:
            {
                if (CarriedItem == null)
                    return;

                await HandleMouseClick(container, server, player, slot);
                break;
            }
            case InventoryOperationMode.ShiftMouseClick:
            {
                if (CarriedItem == null)
                    return;

                //TODO implement shift click

                break;
            }

            case InventoryOperationMode.NumberKeys:
            {
                var localSlot = Button + 36;

                var currentItem = player.Inventory.GetItem(localSlot);

                if (currentItem.IsAir && CarriedItem != null)
                {
                    container.RemoveItem(slot);

                    player.Inventory.SetItem(localSlot, CarriedItem);
                }
                else if (!currentItem.IsAir && CarriedItem != null)
                {
                    container.SetItem(slot, currentItem);

                    player.Inventory.SetItem(localSlot, CarriedItem);
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
                        EntityId = Server.GetNextEntityId(),
                        Item = removedItem,
                        Glowing = true,
                        World = player.World,
                        Position = loc
                    };

                    var lookDir = player.GetLookDirection();
                    var vel = Velocity.FromDirection(loc, lookDir);

                    //TODO Get this shooting out from the player properly.
                    player.World.PacketBroadcaster.QueuePacketToWorld(player.World, new AddEntityPacket
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
                    player.World.PacketBroadcaster.QueuePacketToWorld(player.World, new SetEntityDataPacket
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
                if (CarriedItem == null || CarriedItem.Count >= 64)
                    return;

                TakeFromContainer(container, player.Inventory);
                break;
            }
        }

        if (container is IBlockEntity tileEntityContainer)
        {
            var blockEntity = await player.World.GetBlockEntityAsync(tileEntityContainer.BlockPosition);

            if (blockEntity is not BaseContainer blockEntityContainer)
                return;

            var itemsToBeRemoved = new HashSet<int>();
            var itemsToBeUpdated = new HashSet<NbtCompound>();

            for (int i = 0; i < container.Size; i++)
            {
                var item = container[i];

                if (item is null)
                    continue;

                item.Slot = i;

                blockEntityContainer.SetItem(i, item);
            }
        }
    }

    private void TakeFromContainer(BaseContainer container, BaseContainer playerContainer)
    {
        int amountNeeded = 64 - CarriedItem.Count; // TODO use max item count
        if (amountNeeded == 0)
            return;

        for (int i = 0; i < container.Size; i++)
        {
            ItemStack? item = container[i];
            if (item is null || item != CarriedItem)
                continue;

            int amountTaken = Math.Min(item.Count, amountNeeded);
            item.Count -= amountTaken;

            if (item.Count == 0)
                container.RemoveItem(i);

            CarriedItem.Count += amountTaken;
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
                if (item is null || item != CarriedItem)
                    continue;

                int amountTaken = Math.Min(item.Count, amountNeeded);
                item.Count -= amountTaken;

                if (item.Count == 0)
                    playerContainer.RemoveItem(i);

                CarriedItem.Count += amountTaken;
                amountNeeded -= amountTaken;

                if (amountNeeded == 0)
                    break;
            }
        }
    }

    private async Task HandleMouseClick(BaseContainer container, IServer server, IPlayer player, int slot)
    {
        if (!CarriedItem.IsAir)
        {
            var result = await server.EventDispatcher.ExecuteEventAsync(new ContainerClickEventArgs(player, server, container, CarriedItem)
            {
                Slot = slot
            });

            if (result == EventResult.Cancelled)
                return;

            player.LastClickedItem = CarriedItem;

            container.RemoveItem(slot);
        }
        else
        {
            if (Button == 0)
            {
                container.SetItem(slot, player.LastClickedItem);

                player.LastClickedItem = CarriedItem;
            }
            else
            {
                container.SetItem(slot, player.LastClickedItem);

                player.LastClickedItem = CarriedItem;
            }
        }
    }

    private void HandleDragClick(BaseContainer container, IPlayer player, int value)
    {
        if (ClickedSlot == Outsideinventory)
        {
            player.IsDragging = Button switch
            {
                0 or 4 or 8 => true,
                2 or 6 or 10 => false,
                _ => player.IsDragging
            };
        }
        else if (player.IsDragging)
        {
            if (player.Gamemode == Gamemode.Creative)
            {
                if (Button != 9)
                    return;

                container.SetItem(value, CarriedItem);
            }
            else
            {
                // 1 = left mouse
                // 5 = right mouse
                if (Button != 1 || Button != 5)
                    return;

                container.SetItem(value, CarriedItem);
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
