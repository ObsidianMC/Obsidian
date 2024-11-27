using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;
public sealed partial class ChunkBatchReceivedPacket
{
    [Field(0)]
    public float ChunksPerTick { get; private set; }

    public override void Populate(INetStreamReader reader) => this.ChunksPerTick = reader.ReadFloat();
}
