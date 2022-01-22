using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class TabCompleteRequest : IServerboundPacket
{
    [Field(0), VarLength]
    public int TransactionId { get; private set; }

    [Field(1)]
    public string Text { get; private set; }

    public int Id => 0x06;

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
}
