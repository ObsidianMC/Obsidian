namespace Obsidian.Net.Packets.Common;

public partial record class DisconnectPacket
{
    public required ChatMessage Reason { get; init; }
}


public partial class LoginDisconnectPacket
{
    public required string ReasonJson { get; init; }
}
