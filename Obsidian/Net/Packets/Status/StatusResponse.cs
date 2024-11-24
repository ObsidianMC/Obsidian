using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Status.Clientbound;

public partial class StatusResponsePacket
{
    [Field(0)]
    public string Json { get; }

    public StatusResponsePacket(string json)
    {
        Json = json;
    }

    public StatusResponsePacket(ServerStatus status)
    {
        Json = status.ToJson();
    }

    public override void Serialize(INetStreamWriter writer) => writer.WriteString(this.Json);
}
