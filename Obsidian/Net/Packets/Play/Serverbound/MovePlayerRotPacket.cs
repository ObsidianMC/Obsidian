using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Diagnostics;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class MovePlayerRotPacket
{
    [Field(0), DataFormat(typeof(float))]
    public Angle Yaw { get; private set; }

    [Field(1), DataFormat(typeof(float))]
    public Angle Pitch { get; private set; }

    [Field(2)]
    public bool OnGround { get; private set; }

    public MovePlayerRotPacket()
    {
    }

    public MovePlayerRotPacket(float yaw, float pitch, bool onGround)
    {
        Yaw = yaw;
        Pitch = pitch;
        OnGround = onGround;
    }

    public async override ValueTask HandleAsync(Server server, Player player)
    {
        await player.UpdateAsync(Yaw, Pitch, OnGround);
    }
}
