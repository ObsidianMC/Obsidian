namespace Obsidian.Net.Payloads;

/// <summary>
/// The outcome of negotiating one protocol's channels.
/// </summary>
/// <param name="Channels">On success, each agreed channel id with the version both sides use.</param>
/// <param name="FailureReasons">On failure, why each offending channel couldn't be agreed on.</param>
internal sealed record NegotiationResult(
    IReadOnlyDictionary<string, string> Channels,
    IReadOnlyDictionary<string, string> FailureReasons)
{
    public bool Success => this.FailureReasons.Count == 0;
}

/// <summary>
/// Agrees on the payload channels a server and a client both use for one protocol, with NeoForge's rules so a NeoForge
/// client accepts the same outcome: an optional channel only one side has is dropped, any other channel only one side
/// has fails, and a channel both sides have fails unless their directions and versions match exactly.
/// </summary>
internal static class NetworkComponentNegotiator
{
    public static NegotiationResult Negotiate(
        IReadOnlyCollection<NetworkComponent> server,
        IReadOnlyCollection<NetworkComponent> client)
    {
        var serverById = server.ToDictionary(component => component.Id);
        var clientById = client.ToDictionary(component => component.Id);

        var channels = new Dictionary<string, string>();
        var failures = new Dictionary<string, string>();

        foreach (var serverComponent in server)
        {
            if (!clientById.TryGetValue(serverComponent.Id, out var clientComponent))
            {
                if (!serverComponent.Optional)
                    failures[serverComponent.Id] = "The client is missing this channel.";

                continue;
            }

            if (GetMismatch(serverComponent, clientComponent) is string mismatch)
                failures[serverComponent.Id] = mismatch;
            else
                channels[serverComponent.Id] = serverComponent.Version;
        }

        foreach (var clientComponent in client)
        {
            if (!serverById.ContainsKey(clientComponent.Id) && !clientComponent.Optional)
                failures[clientComponent.Id] = "The server is missing this channel.";
        }

        return failures.Count == 0
            ? new NegotiationResult(channels, failures)
            : new NegotiationResult(new Dictionary<string, string>(), failures);
    }

    private static string? GetMismatch(NetworkComponent server, NetworkComponent client)
    {
        if (server.Flow != client.Flow)
        {
            return $"The channel's direction differs: {Describe(server.Flow)} on the server, "
                + $"{Describe(client.Flow)} on the client.";
        }

        if (server.Version != client.Version)
            return $"The channel's version differs: {server.Version} on the server, {client.Version} on the client.";

        return null;
    }

    private static string Describe(PacketFlow? flow) => flow switch
    {
        PacketFlow.Clientbound => "clientbound",
        PacketFlow.Serverbound => "serverbound",
        _ => "both ways"
    };
}
