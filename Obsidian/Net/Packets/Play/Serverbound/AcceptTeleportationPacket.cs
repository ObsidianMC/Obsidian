using Obsidian.Entities;

namespace Obsidian.Net.Packets.Play.Serverbound;

/// <summary>
/// The client's confirmation of a teleport, with where it put the player: since 26.3 it reports its position and
/// rotation once the teleport, relative parts included, is applied.
/// </summary>
public partial class AcceptTeleportationPacket
{
    // Compatibility value exposed by IPlayer.TeleportId when no destination is pending.
    internal const int NoTeleportAwaited = -1;

    public int TeleportId { get; private set; }

    public VectorD Position { get; private set; }

    public float Yaw { get; private set; }

    public float Pitch { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.TeleportId = reader.ReadVarInt();
        this.Position = VectorD.Read(reader);
        this.Yaw = reader.ReadSingle();
        this.Pitch = reader.ReadSingle();
    }

    public override ValueTask HandleAsync(IServer server, IClientPlayer player) =>
        ((PlayerBase)player).ConfirmTeleportAsync(this.TeleportId, this.Position, this.Yaw, this.Pitch);
}
