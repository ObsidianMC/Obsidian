using Obsidian.Net.Packets.Play.Clientbound;
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

    public async override ValueTask HandleAsync(IServer server, IPlayer player)
    {
        await player.UpdateAsync(Position, this.MovementFlags);
        if (player.Position.ToChunkCoord() != player.LastPosition.ToChunkCoord())
            await player.UpdateChunksAsync();

        player.LastPosition = player.Position;
    }
}
