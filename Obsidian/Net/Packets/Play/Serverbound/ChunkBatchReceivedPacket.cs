using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;
public sealed partial class ChunkBatchReceivedPacket : IServerboundPacket
{
    [Field(0)]
    public float ChunksPerTick { get; set; }

    public int Id => 0x08;

    //TODO impl
    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
}
