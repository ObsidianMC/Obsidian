using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class MovePlayerRotPacket
{
    [Field(0), DataFormat(typeof(float))]
    public Angle Yaw { get; private set; }

    [Field(1), DataFormat(typeof(float))]
    public Angle Pitch { get; private set; }

    [Field(3)]
    public MovementFlags MovementFlags { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.Yaw = reader.ReadFloat();
        this.Pitch = reader.ReadFloat();
        this.MovementFlags = reader.ReadSignedByte<MovementFlags>();
    }

    public async override ValueTask HandleAsync(Server server, Player player)
    {
        await player.UpdateAsync(Yaw, Pitch, this.MovementFlags);
    }
}
