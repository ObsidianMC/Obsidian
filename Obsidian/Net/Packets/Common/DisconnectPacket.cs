namespace Obsidian.Net.Packets.Common;

public partial record class DisconnectPacket
{
    public ChatMessage Reason { get; init; } = default!;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteChat(Reason);
    }
}
