using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class MovePlayerPosPacket
{
    [Field(0), DataFormat(typeof(double))]
    public VectorD Position { get; private set; }

    [Field(1)]
    public MovementFlags MovementFlags { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.Position = VectorD.Read(reader);
        this.MovementFlags = reader.ReadSignedByte<MovementFlags>();
    }

    public override ValueTask HandleAsync(IServer server, IClientPlayer player) =>
        ((PlayerBase)player).HandleClientMovementAsync(this.Position, null, null, this.MovementFlags);
}
