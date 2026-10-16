using Obsidian.API;
using Obsidian.API.Networking;

namespace EngineSample.Charms;

/// <summary>
/// What the client's charm key sends: a request to open the player's charm slots. It carries nothing.
/// </summary>
public sealed record OpenCharms : ICustomPayload<OpenCharms>
{
    public static string Channel => "sample:open_charms";

    public static OpenCharms Read(INetStreamReader reader) => new();

    public void Write(INetStreamWriter writer)
    {
    }
}
