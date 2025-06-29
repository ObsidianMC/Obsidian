using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class MovePlayerPosRotPacket
{
    [Field(0), DataFormat(typeof(double))]
    public VectorF Position { get; private set; }

    [Field(1), DataFormat(typeof(float))]
    public Angle Yaw { get; private set; }

    [Field(2), DataFormat(typeof(float))]
    public Angle Pitch { get; private set; }

    [Field(3)]
    public MovementFlags MovementFlags { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.Position = reader.ReadAbsolutePositionF();
        this.Yaw = reader.ReadSingle();
        this.Pitch = reader.ReadSingle();
        this.MovementFlags = reader.ReadSignedByte<MovementFlags>();
    }

    public async override ValueTask HandleAsync(IServer server, IPlayer player)
    {
        // The first time we get this packet, it doesn't make sense so we should ignore it.
        if (player.LastPosition == Vector.Zero) { return; }

        await player.UpdateAsync(Position, Yaw, Pitch, this.MovementFlags);
        if (player.Position.ToChunkCoord() != player.LastPosition.ToChunkCoord())
        {
            (int cx, int cz) = player.Position.ToChunkCoord();
            await player.Client.QueuePacketAsync(new SetChunkCacheCenterPacket(cx, cz));
        }

        player.LastPosition = player.Position;
    }
}
