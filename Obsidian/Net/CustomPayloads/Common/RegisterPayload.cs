namespace Obsidian.Net.CustomPayloads.Common;

public class RegisterPayload : ICustomPayload
{
    public string ResourceLocation => "c:register";

    public ValueTask HandleAsync(IServer server) => default;
}
