using Microsoft.Extensions.Logging;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class ChatMessagePacket : IServerboundPacket
{
    [Field(0)]
    public string Message { get; private set; }

    [Field(1)]
    public DateTimeOffset Timestamp { get; private set; }

    [Field(2)]
    public long Salt { get; private set; }

    [Field(4)]
    public byte[] Signature { get; private set; }

    [Field(5)]
    public bool SignedPreview { get; set; }

    public int Id => 0x04;

    public async ValueTask HandleAsync(Server server, Player player)
    {
        await server.HandleIncomingMessageAsync(this, player.client);
    }
}
