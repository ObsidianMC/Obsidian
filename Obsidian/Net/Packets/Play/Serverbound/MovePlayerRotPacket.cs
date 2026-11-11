using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class MovePlayerRotPacket
{
    [Field(0), DataFormat(typeof(float))]
    public float Yaw { get; private set; }

    [Field(1), DataFormat(typeof(float))]
    public float Pitch { get; private set; }

    [Field(3)]
    public MovementFlags MovementFlags { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.Yaw = reader.ReadSingle();
        this.Pitch = reader.ReadSingle();
        this.MovementFlags = reader.ReadSignedByte<MovementFlags>();
    }

    public override ValueTask HandleAsync(IServer server, IClientPlayer player) =>
        ((PlayerBase)player).HandleClientMovementAsync(null, this.Yaw, this.Pitch, this.MovementFlags);
}
