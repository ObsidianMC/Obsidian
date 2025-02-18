namespace Obsidian.API.Handlers;
public abstract class CustomPayloadHandler(IServer server)
{
    protected Dictionary<string, ICustomPayload> Payloads { get; } = [];
    protected IServer Server { get; } = server;

    public bool TryAddPayload(ICustomPayload payload) => this.Payloads.TryAdd(payload.ResourceLocation, payload);

    public bool RemovePayload(string resourceLocation) => this.Payloads.Remove(resourceLocation);

    public async virtual ValueTask ExecutePayloadAsync(string resourceLocation)
    {
        var payload = this.Payloads.GetValueOrDefault(resourceLocation);

        if (payload is null)
            return;

        await payload.HandleAsync(this.Server);
    }
}
