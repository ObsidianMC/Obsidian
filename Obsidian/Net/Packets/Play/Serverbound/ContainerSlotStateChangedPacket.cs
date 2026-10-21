namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class ContainerSlotStateChangedPacket
{
    public int SlotId { get; private set; }

    public int ContainerId { get; private set; }

    /// <summary>
    /// Whether the slot is now enabled.
    /// </summary>
    public bool NewState { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.SlotId = reader.ReadVarInt();
        this.ContainerId = reader.ReadVarInt();
        this.NewState = reader.ReadBoolean();
    }

    public override ValueTask HandleAsync(IServer server, IClientPlayer player) =>
        Obsidian.Events.MainEventHandler.SetCrafterSlotStateAsync(player, this.ContainerId, this.SlotId, this.NewState);
}
