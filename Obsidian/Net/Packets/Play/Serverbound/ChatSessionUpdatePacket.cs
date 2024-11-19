using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;
public sealed partial class ChatSessionUpdatePacket
{
    [Field(0)]
    public Guid SessionId { get; set; }

    [Field(1)]
    public SignatureData SignatureData { get; set; }

    public override ValueTask HandleAsync(Server server, Player player)
    {
        player.client.signatureData = this.SignatureData;

        return default;
    }
}
