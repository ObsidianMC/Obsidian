namespace Obsidian.Net.CustomPayloads.Common;

public class VersionPayload : ICustomPayload
{
    public string ResourceLocation => "c:version";

    public ValueTask HandleAsync(IServer server) => default;
}
