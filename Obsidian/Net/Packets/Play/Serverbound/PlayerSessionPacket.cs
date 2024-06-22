using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;
public sealed partial class PlayerSessionPacket : IServerboundPacket
{
    [Field(0)]
    public Guid SessionId { get; set; }

    [Field(1)]
    public SignatureData SignatureData { get; set; }

    public int Id => 0x07;

    public ValueTask HandleAsync(Server server, Player player)
    {
        player.client.signatureData = this.SignatureData;

        return ValueTask.CompletedTask;
    }
}
