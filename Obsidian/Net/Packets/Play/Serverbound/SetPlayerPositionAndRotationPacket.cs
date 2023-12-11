using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class SetPlayerPositionAndRotationPacket : IServerboundPacket
{
    [Field(0), DataFormat(typeof(double))]
    public VectorF Position { get; private set; }

    [Field(1), DataFormat(typeof(float))]
    public Angle Yaw { get; private set; }

    [Field(2), DataFormat(typeof(float))]
    public Angle Pitch { get; private set; }

    [Field(3)]
    public bool OnGround { get; private set; }

    public int Id => 0x18;

    public async ValueTask HandleAsync(Server server, Player player)
    {
        await player.UpdateAsync(Position, Yaw, Pitch, OnGround);
        if (player.Position.ToChunkCoord() != player.LastPosition.ToChunkCoord())
        {
            (int cx, int cz) = player.Position.ToChunkCoord();
            await player.client.QueuePacketAsync(new SetCenterChunkPacket(cx, cz));
        }

        player.LastPosition = player.Position;
    }
}
