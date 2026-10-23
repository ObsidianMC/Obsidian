using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;
public sealed partial class ChunkBatchReceivedPacket
{
    [Field(0)]
    public float ChunksPerTick { get; private set; }

    public override void Populate(INetStreamReader reader) => this.ChunksPerTick = reader.ReadSingle();

    public override ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        (player as Player)?.OnChunkBatchReceived(this.ChunksPerTick);
        return default;
    }
}
