using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class MovePlayerPosPacket
{
    [Field(0), DataFormat(typeof(double))]
    public VectorF Position { get; private set; }

    [Field(1)]
    public MovementFlags MovementFlags { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.Position = reader.ReadAbsolutePositionF();
        this.MovementFlags = reader.ReadSignedByte<MovementFlags>();
    }

    public async override ValueTask HandleAsync(Server server, Player player)
    {
        await player.UpdateAsync(Position, this.MovementFlags);
        if (player.Position.ToChunkCoord() != player.LastPosition.ToChunkCoord())
        {
            await player.UpdateChunksAsync(distance: player.ClientInformation.ViewDistance);
            (int cx, int cz) = player.Position.ToChunkCoord();
            await player.client.QueuePacketAsync(new SetChunkCacheCenterPacket(cx, cz));
        }

        player.LastPosition = player.Position;
    }
}
