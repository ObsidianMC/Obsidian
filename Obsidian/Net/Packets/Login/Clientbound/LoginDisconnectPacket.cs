
namespace Obsidian.Net.Packets.Login.Clientbound;
public partial class LoginDisconnectPacket
{
    public required string ReasonJson { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteString(ReasonJson);
    }
}
