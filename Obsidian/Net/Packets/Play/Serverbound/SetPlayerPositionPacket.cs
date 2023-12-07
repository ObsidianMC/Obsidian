using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class SetPlayerPositionPacket : IServerboundPacket
{
    [Field(0), DataFormat(typeof(double))]
    public VectorF Position { get; private set; }

    [Field(1)]
    public bool OnGround { get; private set; }

    public int Id => 0x17;

    public SetPlayerPositionPacket()
    {
    }

    public SetPlayerPositionPacket(VectorF position, bool onGround)
    {
        Position = position;
        OnGround = onGround;
    }

    public async ValueTask HandleAsync(Server server, Player player)
    {
        await player.UpdateAsync(Position, OnGround);
        if (player.Position.ToChunkCoord() != player.LastPosition.ToChunkCoord())
        {
            await player.UpdateChunksAsync(distance: player.ClientInformation.ViewDistance);
            (int cx, int cz) = player.Position.ToChunkCoord();
            player.client.SendPacket(new SetCenterChunkPacket(cx, cz));
        }

        player.LastPosition = player.Position;
    }
}
