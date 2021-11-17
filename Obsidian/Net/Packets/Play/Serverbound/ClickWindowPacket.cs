using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;

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
    public IDictionary<short, ItemStack> Slots { get; private set; }

    /// <summary>
    /// The clicked slot. Has to be empty (item ID = -1) for drop mode.
    /// </summary>
    [Field(6)]
    public ItemStack ClickedItem { get; private set; }

    public int Id => 0x08;

    public async ValueTask HandleAsync(Server server, Player player)
    {
        var inventory = player.OpenedInventory ?? player.Inventory;

        var (value, forPlayer) = ClickedSlot.GetDifference(inventory.Size);

        //Uhh this isn't supposed to be like this 😅
        if (WindowId == 0 && player.LastClickedBlock.Is(Material.EnderChest) && ClickedSlot >= 27 && ClickedSlot <= 62 || forPlayer)
            inventory = player.Inventory;

        switch (Mode)
        {
            case InventoryOperationMode.MouseClick:
                await HandleMouseClick(inventory, server, player, value);
                break;

            case InventoryOperationMode.ShiftMouseClick:
                {
                    if (ClickedItem == null)
                        return;

                    inventory.RemoveItem(value);
                    player.Inventory.AddItem(ClickedItem);
                    break;
                }

            case InventoryOperationMode.NumberKeys:
                {
                    var localSlot = Button + 36;

                    var currentItem = player.Inventory.GetItem(localSlot);

                    if (currentItem.IsAir() && ClickedItem != null)
                    {
                        inventory.RemoveItem(value);

                        player.Inventory.SetItem(localSlot, ClickedItem);
                    }
                    else if (!currentItem.IsAir() && ClickedItem != null)
                    {
                        inventory.SetItem(value, currentItem);

                        player.Inventory.SetItem(localSlot, ClickedItem);
                    }
                    else
                    {
                        inventory.SetItem(value, currentItem);

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
                        ItemStack removedItem = null;
                        if (Button == 0)
                            inventory.TryRemoveItem(value, out removedItem);
                        else
                            inventory.TryRemoveItem(value, 64, out removedItem);

                        if (removedItem == null)
                            return;

                        var loc = new VectorF(player.Position.X, (float)player.HeadY - 0.3f, player.Position.Z);

                        var item = new ItemEntity
                        {
                            EntityId = player + player.World.TotalLoadedEntities() + 1,
                            Count = 1,
                            Id = removedItem.AsItem().Id,
                            Glowing = true,
                            World = player.World,
                            Position = loc
                        };

                        player.World.TryAddEntity(item);

                        server.BroadcastPacket(new SpawnEntityPacket
                        {
                            EntityId = item.EntityId,
                            Uuid = item.Uuid,
                            Type = EntityType.Item,
                            Position = item.Position,
                            Pitch = 0,
                            Yaw = 0,
                            Data = 1,
                            Velocity = Velocity.FromVector(player.Position + new VectorF(
                                (Globals.Random.NextFloat() * 0.5f) + 0.25f,
                                (Globals.Random.NextFloat() * 0.5f) + 0.25f,
                                (Globals.Random.NextFloat() * 0.5f) + 0.25f))
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
                HandleDragClick(inventory, player, value);
                break;

            case InventoryOperationMode.DoubleClick:
                {
                    if (ClickedItem == null || ClickedItem.Count >= 64)
                        return;

                    var item = ClickedItem;

                    (ItemStack item, int index) selectedItem = (null, 0);

                    var items = inventory.Items
                        .Select((item, index) => (item, index))
                        .Where(tuple => tuple.item.Type == item.Type)
                        .OrderByDescending(x => x.index);

                    foreach (var (invItem, index) in items)
                    {
                        if (invItem != item)
                            continue;

                        var copyItem = invItem;

                        var finalCount = item.Count + copyItem.Count;

                        if (finalCount <= 64)
                        {
                            item += copyItem.Count;

                            copyItem -= finalCount;
                        }
                        else if (finalCount > 64)
                        {
                            var difference = finalCount - 64;

                            copyItem -= difference;

                            item += difference;
                        }

                        selectedItem = (copyItem, index);
                        break;
                    }

                    inventory.SetItem((short)selectedItem.index, selectedItem.item);
                    break;
                }
        }
    }

    private async Task HandleMouseClick(Inventory inventory, Server server, Player player, int value)
    {
        if (!ClickedItem.IsAir())
        {
            var @event = await server.Events.InvokeInventoryClickAsync(new InventoryClickEventArgs(player, inventory, ClickedItem)
            {
                Slot = value
            });

            if (@event.Cancel)
                return;

            player.LastClickedItem = ClickedItem;

            inventory.SetItem(value, null);
        }
        else
        {
            if (Button == 0)
            {
                inventory.SetItem(value, player.LastClickedItem);

                // if (!inventory.OwnedByPlayer)
                //    Globals.PacketLogger.LogDebug($"{(inventory.HasItems() ? JsonConvert.SerializeObject(inventory.Items.Where(x => x != null), Formatting.Indented) : "No Items")}");

                player.LastClickedItem = ClickedItem;
            }
            else
            {
                inventory.SetItem(value, player.LastClickedItem);

                // if (!inventory.OwnedByPlayer)
                //    Globals.PacketLogger.LogDebug($"{(inventory.HasItems() ? JsonConvert.SerializeObject(inventory.Items.Where(x => x != null), Formatting.Indented) : "No Items")}");

                player.LastClickedItem = ClickedItem;
            }
        }
    }

    private void HandleDragClick(Inventory inventory, Player player, int value)
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

                inventory.SetItem(value, ClickedItem);
            }
            else
            {
                // 1 = left mouse
                // 5 = right mouse
                if (Button != 1 || Button != 5)
                    return;

                inventory.SetItem(value, ClickedItem);
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
