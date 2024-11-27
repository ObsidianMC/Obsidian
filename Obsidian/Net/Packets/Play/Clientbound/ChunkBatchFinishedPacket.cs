using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;
public partial class ChunkBatchFinishedPacket
{
    [Field(0), VarLength]
    public int BatchSize { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.BatchSize);
    }
}
