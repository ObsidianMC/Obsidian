using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class PlayerRotation : IServerboundPacket
{
    [Field(0), DataFormat(typeof(float))]
    public Angle Yaw { get; private set; }

    [Field(1), DataFormat(typeof(float))]
    public Angle Pitch { get; private set; }

    [Field(2)]
    public bool OnGround { get; private set; }

    public int Id => 0x13;

    public PlayerRotation()
    {
    }

    public PlayerRotation(float yaw, float pitch, bool onGround)
    {
        Yaw = yaw;
        Pitch = pitch;
        OnGround = onGround;
    }

    public void Populate(byte[] data) => throw new NotImplementedException();

    public void Populate(MinecraftStream stream) => throw new NotImplementedException();

    public async ValueTask HandleAsync(Server server, Player player)
    {
        await player.UpdateAsync(Yaw, Pitch, OnGround);
    }
}
