using Obsidian.API.Events;
using Obsidian.API.Inventory;
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
    public IDictionary<short, IHashedItemStack?> ChangedSlots { get; private set; } = default!;

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

        await server.EventDispatcher.ExecuteEventAsync(new ContainerClickEventArgs(player, server, container)
        {
            ClickedSlot = slot,
            ClickType = this.ClickType,
            ChangedSlots = this.ChangedSlots.AsReadOnly(),
            Button = this.Button,
            StateId = this.StateId,
            CarriedItem = this.CarriedItem,
            ContainerId = this.ContainerId,
        });
    }
}

