using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.Entities;
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

    public async override ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        if (this.ContainerId != (player.OpenedContainer is null ? 0 : player.CurrentContainerId))
            return;

        if (player.OpenedContainer is Obsidian.Entities.MerchantContainer merchant)
        {
            await merchant.HandleClickAsync(this, player);
            return;
        }

        var window = player.OpenedContainer ?? player.Inventory;
        var (slot, forPlayer) = window.GetSlot(this.ClickedSlot);

        var container = (this.IsPlayerInventory || forPlayer) ? player.Inventory : window;

        if (this.ClickedSlot != -999 && (this.ClickedSlot < 0 || slot < 0 || slot >= container.Size ||
            (!this.IsPlayerInventory && forPlayer && slot >= 45)))
        {
            return;
        }

        var click = new ContainerClickEventArgs(player, server, container)
        {
            ClickedSlot = slot,
            WindowSlot = this.ClickedSlot,
            ClickType = this.ClickType,
            Button = this.Button,
            StateId = this.StateId,
            ContainerId = this.ContainerId,
        };

        // What the click applies to: the server applies it only if the window, the player's inventory and the cursor
        // are unchanged when its turn comes. Read in turn with other clicks, so it's never half of one.
        await using (DeferredInventoryWork.Begin(player.Client.Logger))
        using (await BaseContainer.LockWindowAsync(window, player.Inventory))
            click.Arrive(NetworkBuffer.SnapshotItemStack);

        // Plugins' handlers run without the container's lock, so that one can wait for another player's click; the
        // server's own handler, which runs last, takes it to apply the click and sends the window to its viewers,
        // even when the click is cancelled (see MainEventHandler.OnInventoryClick). Client slot hashes describe its
        // prediction, not authoritative inventory changes, so they're not used.
        await server.EventDispatcher.ExecuteEventAsync(click);
    }
}
