namespace Obsidian.API;

public interface ICustomPayload
{
    public string ResourceLocation { get; }

    public ValueTask HandleAsync(IServer server);
}
