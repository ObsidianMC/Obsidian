namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class SetBeaconPacket
{
    /// <summary>
    /// The effect ids of the powers chosen, if any.
    /// </summary>
    public int? Primary { get; private set; }

    public int? Secondary { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.Primary = reader.ReadBoolean() ? reader.ReadVarInt() : null;
        this.Secondary = reader.ReadBoolean() ? reader.ReadVarInt() : null;
    }

    public override ValueTask HandleAsync(IServer server, IClientPlayer player) =>
        Obsidian.Events.MainEventHandler.SetBeaconAsync(player, this.Primary, this.Secondary);
}
