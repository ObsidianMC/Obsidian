using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;
public sealed partial class ChunkBatchFinishedPacket : IClientboundPacket
{
    [Field(0), VarLength]
    public int BatchSize { get; init; }

    public int Id => 0x0C;
}
