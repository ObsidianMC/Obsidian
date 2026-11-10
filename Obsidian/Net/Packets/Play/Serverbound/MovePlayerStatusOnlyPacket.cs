using Obsidian.Entities;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class MovePlayerStatusOnlyPacket
{
    public MovementFlags MovementFlags { get; private set; }

    public override void Populate(INetStreamReader reader) =>
        this.MovementFlags = reader.ReadSignedByte<MovementFlags>();

    public override ValueTask HandleAsync(IServer server, IClientPlayer player) =>
        ((PlayerBase)player).HandleClientMovementAsync(null, null, null, this.MovementFlags);
}
