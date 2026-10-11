using Obsidian.API.Networking;
using Obsidian.API.Plugins;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.Net.Payloads;

/// <summary>
/// A plugin's payload channel, with the handler that decodes and handles what clients send on it.
/// </summary>
internal sealed class PayloadRegistration
{
    public required string Channel { get; init; }
    public required PayloadPhase Phase { get; init; }
    public required PayloadDirection Direction { get; init; }
    public required string Version { get; init; }
    public required bool Optional { get; init; }
    public required IPluginContainer Owner { get; init; }

    /// <summary>
    /// Decodes a payload from the bytes after the channel id and handles it, or null if the plugin doesn't receive it.
    /// </summary>
    public Func<INetStreamReader, PayloadContext, ValueTask>? Handler { get; init; }

    public NetworkComponent ToComponent() => new(this.Channel, this.Version, this.Direction.ToFlow(), this.Optional);
}

/// <summary>
/// The payload channels plugins registered. Connections look channels up here each time a payload arrives, so a
/// plugin can be unloaded or reloaded while players stay connected; payloads for a channel that's gone are dropped.
/// </summary>
/// <remarks>Reads are lock-free snapshots; registering and unregistering may run concurrently with them.</remarks>
internal sealed class PayloadChannelRegistry
{
    private ImmutableDictionary<string, PayloadRegistration> registrations =
        ImmutableDictionary<string, PayloadRegistration>.Empty;

    /// <summary>
    /// Whether any plugin registered a channel; the status response reports the server as modded then.
    /// </summary>
    public bool HasChannels => !this.registrations.IsEmpty;

    /// <summary>
    /// Registers <typeparamref name="TPayload"/>'s channel for <paramref name="owner"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The channel is already registered.</exception>
    public void Register<TPayload>(IPluginContainer owner, PayloadPhase phase, PayloadDirection direction,
        PayloadHandler<TPayload>? handler, string version, bool optional)
        where TPayload : ICustomPayload<TPayload>
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrEmpty(version);

        var channel = TPayload.Channel;

        if (!channel.Contains(':'))
            throw new ArgumentException($"Payload channel '{channel}' must be a namespaced id like 'mymod:sync'.");

        if (handler is not null && direction == PayloadDirection.Clientbound)
            throw new ArgumentException($"Payload channel '{channel}' is clientbound, so it can't have a handler.");

        var registration = new PayloadRegistration
        {
            Channel = channel,
            Phase = phase,
            Direction = direction,
            Version = version,
            Optional = optional,
            Owner = owner,
            Handler = handler is null
                ? null
                : (reader, context) => handler(TPayload.Read(reader), context)
        };

        if (!ImmutableInterlocked.TryAdd(ref this.registrations, channel, registration))
            throw new InvalidOperationException($"Payload channel '{channel}' is already registered.");
    }

    /// <summary>
    /// Removes every channel <paramref name="plugin"/> registered; call it when the plugin unloads.
    /// </summary>
    public void UnregisterPluginPayloads(IPluginContainer plugin)
    {
        ImmutableInterlocked.Update(ref this.registrations, registrations =>
        {
            var owned = registrations.Values
                .Where(registration => registration.Owner == plugin)
                .Select(registration => registration.Channel);

            return registrations.RemoveRange(owned);
        });
    }

    public bool TryGet(string channel, [MaybeNullWhen(false)] out PayloadRegistration registration) =>
        this.registrations.TryGetValue(channel, out registration);

    /// <summary>
    /// The server's declarations for <paramref name="protocol"/>, to negotiate with a client's.
    /// </summary>
    public List<NetworkComponent> GetComponents(NetworkProtocol protocol)
    {
        var components = this.registrations.Values
            .Where(registration => registration.Phase.Includes(protocol))
            .Select(registration => registration.ToComponent())
            .ToList();

        if (protocol == NetworkProtocol.Configuration)
            components.AddRange(ChannelPayloads.BuiltinComponents);

        return components;
    }
}
