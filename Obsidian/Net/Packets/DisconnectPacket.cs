using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets;

public partial class DisconnectPacket : IClientboundPacket
{
    [Field(0)]
    private ChatMessage Reason { get; }

    public int Id { get; }

    public DisconnectPacket(ChatMessage reason, ClientState state)
    {
        Id = state == ClientState.Configuration ? 0x01 : state == ClientState.Play ? 0x1A : 0x00;
        Reason = reason;
    }
}
