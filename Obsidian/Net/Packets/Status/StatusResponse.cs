using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Status.Clientbound;

public partial class StatusResponse
{
    [Field(0)]
    public string Json { get; }

    public StatusResponse(string json)
    {
        Json = json;
    }

    public StatusResponse(ServerStatus status)
    {
        Json = status.ToJson();
    }
}
