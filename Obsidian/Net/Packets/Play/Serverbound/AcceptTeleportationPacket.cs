using Obsidian.Entities;

namespace Obsidian.Net.Packets.Play.Serverbound;

/// <summary>
/// The client's confirmation of a teleport, with where it put the player: since 26.3 it reports its position and
/// rotation once the teleport, relative parts included, is applied.
/// </summary>
public partial class AcceptTeleportationPacket
{
    // Kept in the player's TeleportId once a teleport is confirmed. Obsidian's ids are never negative.
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

    public async override ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        // Like vanilla's handleAcceptTeleportPacket, a confirmation of an older teleport is ignored.
        if (this.TeleportId != player.TeleportId)
            return;

        // A confirmation when no teleport is awaited, or with a position that isn't a number, disconnects.
        if (this.TeleportId == NoTeleportAwaited || double.IsNaN(this.Position.X) || double.IsNaN(this.Position.Y) ||
            double.IsNaN(this.Position.Z) || !float.IsFinite(this.Yaw) || !float.IsFinite(this.Pitch))
        {
            await player.KickAsync(new ChatMessage { Translate = "multiplayer.disconnect.invalid_player_movement" });
            return;
        }

        player.TeleportId = NoTeleportAwaited;

        // The server already put the player at the teleport's destination; the client's report then goes through
        // like any move, as vanilla does with handlePlayerPositionChange, neither on the ground nor against a wall.
        var flags = player.MovementFlags & ~(MovementFlags.OnGround | MovementFlags.HorizontalCollision);
        await player.UpdateAsync(this.Position, this.Yaw, this.Pitch, flags);
        if (player.Position.ToChunkCoord() != player.LastPosition.ToChunkCoord())
            await player.UpdateChunksAsync();

        player.LastPosition = player.Position;
    }
}
