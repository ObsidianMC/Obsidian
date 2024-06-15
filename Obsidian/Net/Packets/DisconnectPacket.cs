using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets;

public partial class DisconnectPacket : IClientboundPacket
{
    private readonly ClientState state;

    [Field(0), Condition("!IsLoginState")]
    private ChatMessage Reason { get; }

    [Field(1), Condition("IsLoginState")]
    private string ReasonJson { get; }

    public bool IsLoginState => this.state == ClientState.Login;

    public int Id { get; }

    public DisconnectPacket(ChatMessage reason, ClientState state)
    {
        Id = state == ClientState.Configuration ? 0x01 : state == ClientState.Play ? 0x1D : 0x00;

        Reason = reason;
        ReasonJson = reason.ToString(Globals.JsonOptions);

        this.state = state;
    }
}
