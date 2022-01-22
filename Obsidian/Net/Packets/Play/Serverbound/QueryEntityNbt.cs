using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class QueryEntityNbt : IServerboundPacket
{
    [Field(0), VarLength]
    public int TransactionId { get; set; }

    [Field(1), VarLength]
    public int EntityId { get; set; }

    public int Id => 0x15;

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
}
