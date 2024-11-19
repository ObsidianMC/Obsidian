using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class MovePlayerPosPacket
{
    [Field(0), DataFormat(typeof(double))]
    public VectorF Position { get; private set; }

    [Field(1)]
    public bool OnGround { get; private set; }
    public MovePlayerPosPacket()
    {
    }

    public MovePlayerPosPacket(VectorF position, bool onGround)
    {
        Position = position;
        OnGround = onGround;
    }

    public async override ValueTask HandleAsync(Server server, Player player)
    {
        await player.UpdateAsync(Position, OnGround);
        if (player.Position.ToChunkCoord() != player.LastPosition.ToChunkCoord())
        {
            await player.UpdateChunksAsync(distance: player.ClientInformation.ViewDistance);
            (int cx, int cz) = player.Position.ToChunkCoord();
            await player.client.QueuePacketAsync(new SetChunkCacheCenterPacket(cx, cz));
        }

        player.LastPosition = player.Position;
    }
}
