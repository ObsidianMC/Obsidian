using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Status;

public partial class RequestResponse : IClientboundPacket
{
    [Field(0)]
    public string Json { get; }

    public int Id => 0x00;

    public RequestResponse(string json)
    {
        Json = json;
    }

    public RequestResponse(ServerStatus status)
    {
        Json = status.ToJson();
    }
}
