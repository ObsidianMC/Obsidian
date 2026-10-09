using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.API.Containers;
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
        if (this.ContainerId != (player.OpenedContainer is null ? 0 : player.CurrentContainerId))
            return;

        if (player.OpenedContainer is Obsidian.Entities.MerchantContainer merchant)
        {
            await merchant.HandleClickAsync(this, player);
            return;
        }

        var baseContainer = player.OpenedContainer ?? player.Inventory;

        var (slot, forPlayer) = baseContainer.GetSlot(ClickedSlot);

        var container = (this.IsPlayerInventory || forPlayer) ? player.Inventory : baseContainer;

        if (this.ClickedSlot != -999 && (this.ClickedSlot < 0 || slot < 0 || slot >= container.Size ||
            (!this.IsPlayerInventory && forPlayer && slot >= 45)))
            return;

        if (this.IsPlayerInventory || baseContainer is CraftingTable { Type: InventoryType.Crafting })
        {
            if (this.ClickType == ClickType.QuickCraft && this.ClickedSlot == -999)
                player.IsDragging = DraggingButtons.Contains(this.Button);

            await server.EventDispatcher.ExecuteEventAsync(new ContainerClickEventArgs(player, server, container)
            {
                ClickedSlot = slot,
                ClickType = this.ClickType,
                Button = this.Button,
                StateId = this.StateId,
                ContainerId = this.ContainerId,
            });
            return;
        }

        if (this.ClickType == ClickType.QuickCraft && ClickedSlot == -999)
            player.IsDragging = DraggingButtons.Contains(Button);

        await server.EventDispatcher.ExecuteEventAsync(new ContainerClickEventArgs(player, server, container)
        {
            ClickedSlot = slot,
            ClickType = this.ClickType,
            Button = this.Button,
            StateId = this.StateId,
            ContainerId = this.ContainerId,
        });

        // Client slot hashes describe its prediction, not authoritative inventory changes.
        var contents = baseContainer.ToList();
        contents.AddRange(player.Inventory.Skip(9).Take(36));
        await player.RequireClient("player operation").QueuePacketAsync(new ContainerSetContentPacket(this.ContainerId, contents)
        {
            StateId = this.StateId + 1,
            CarriedItem = player.CarriedItem
        });
    }

    private static readonly sbyte[] DraggingButtons = [0, 4, 8];

}

