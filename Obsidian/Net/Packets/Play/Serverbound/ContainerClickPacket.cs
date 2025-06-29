using Microsoft.Extensions.Logging;
using Obsidian.API.Events;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

// Source: https://wiki.vg/index.php?title=Protocol&oldid=14889#Click_Window
public partial class ContainerClickPacket
{
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
    public ClickType ClickType { get; private set; }

    [Field(5)]
    public IDictionary<short, IHashedItemStack?> ChangedSlots { get; private set; } = new Dictionary<short, IHashedItemStack?>();

    /// <summary>
    /// 	Item carried by the cursor. Has to be empty (item ID = -1) for drop mode, otherwise nothing will happen.
    /// </summary>
    [Field(6)]
    public IHashedItemStack? CarriedItem { get; private set; }

    private bool IsPlayerInventory => this.ContainerId == 0;

    public override void Populate(INetStreamReader reader)
    {
        this.ContainerId = reader.ReadVarInt();
        this.StateId = reader.ReadVarInt();
        this.ClickedSlot = reader.ReadShort();
        this.Button = reader.ReadSignedByte();
        this.ClickType = reader.ReadVarInt<ClickType>();

        var length = reader.ReadVarInt();

        this.ChangedSlots = new Dictionary<short, IHashedItemStack?>(length);
        for (int i = 0; i < length; i++)
            this.ChangedSlots.Add(reader.ReadShort(), reader.ReadHashedItemStack());

        this.CarriedItem = reader.ReadHashedItemStack();
    }

    public async override ValueTask HandleAsync(IServer server, IPlayer player)
    {
        var container = player.OpenedContainer ?? player.Inventory;

        var (slot, forPlayer) = container.GetDifference(ClickedSlot);

        if (this.IsPlayerInventory || forPlayer)
            container = player.Inventory;

        var clickedItem = container[slot];

        // Maybe we should have an event called ValidateContainerContentsEventArgs? 
        if (!this.CarriedItem.Compare(clickedItem))
        {
            player.Client.Logger.LogWarning("Item being carried does not match the one that was picked up from the inventory.");

            //The items don't match sync the client back.
            await player.Client.QueuePacketAsync(new ContainerSetSlotPacket
            {
                ContainerId = this.ContainerId,
                Slot = -1,
                SlotData = clickedItem,
                StateId = 0,//State id is ignored if slot is set to -1
            });
        }

        var invalidItems = new Dictionary<short, IHashedItemStack>();
        foreach (var (changedSlot, hashedItem) in this.ChangedSlots)
        {
            var checkedItem = container[changedSlot];

            if(hashedItem == null)
            {
                container.RemoveItem(changedSlot);
                continue;
            }

            if (!hashedItem.Compare(checkedItem))
                invalidItems.Add(changedSlot, hashedItem);
        }

        if (invalidItems.Count > 0)
        {
            player.Client.Logger.LogWarning("Out of sync inventory. Contained {count} items that were out of sync.", invalidItems.Count);

            await player.Client.QueuePacketAsync(new ContainerSetContentPacket(this.ContainerId, container.ToList())
            {
                StateId = this.StateId,
                CarriedItem = clickedItem
            });
        }

        await server.EventDispatcher.ExecuteEventAsync(new ContainerClickEventArgs(player, server, container)
        {
            ClickedSlot = slot,
            ClickType = this.ClickType,
            Button = this.Button,
            StateId = this.StateId,
            ContainerId = this.ContainerId,
        });
    }
}

