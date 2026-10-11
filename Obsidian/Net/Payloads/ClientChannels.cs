using Microsoft.Extensions.Logging;
using Obsidian.API.Networking;
using Obsidian.Net.Packets.Common;
using Obsidian.Plugins;

namespace Obsidian.Net.Payloads;

/// <summary>
/// One connection's payload channels: NeoForge's configuration handshake, the channels it agreed on, the channels the
/// client registered itself, and receiving and sending payloads on them.
/// </summary>
/// <remarks>
/// Only channel ids and versions are kept here, never plugin types, so the connection outlives a plugin reload. The
/// handshake runs on the connection's packet loop; the state it leaves behind may be read from any thread.
/// </remarks>
internal sealed partial class ClientChannels(IClient client, PayloadChannelRegistry registry)
{
    private const int HandshakePingId = 0;

    // NeoForge's server asks with an empty query; the client answers with all of its channels.
    private static readonly ImmutableDictionary<NetworkProtocol, List<NetworkComponent>> EmptyQuery =
        ImmutableDictionary<NetworkProtocol, List<NetworkComponent>>.Empty;

    private static readonly ImmutableArray<NetworkProtocol> NegotiatedProtocols =
        [NetworkProtocol.Configuration, NetworkProtocol.Play];

    private ImmutableDictionary<string, string> configurationChannels = ImmutableDictionary<string, string>.Empty;
    private ImmutableDictionary<string, string> playChannels = ImmutableDictionary<string, string>.Empty;
    private ImmutableHashSet<string> clientChannels = ImmutableHashSet<string>.Empty;

    private bool handshakeFinished;

    // Set when the NeoForge negotiation refused the client, so configuration doesn't go on.
    private bool refused;

    /// <summary>
    /// Whether the client answered with a NeoForge channel query, rather than being a vanilla (or other) client.
    /// </summary>
    public bool IsNeoForge { get; private set; }

    /// <summary>
    /// Whether the connection may carry payloads on <paramref name="channel"/> in <paramref name="protocol"/>. Like
    /// NeoForge, vanilla and handshake channels always may; others must have been agreed on in the handshake or
    /// registered by the client with <c>minecraft:register</c>.
    /// </summary>
    public bool CanUse(string channel, NetworkProtocol protocol)
    {
        if (channel.StartsWith("minecraft:", StringComparison.Ordinal)
            || channel is ChannelPayloads.NeoForgeQuery
                or ChannelPayloads.NeoForgeNetwork
                or ChannelPayloads.NeoForgeSetupFailed)
        {
            return true;
        }

        var negotiated = protocol == NetworkProtocol.Configuration ? this.configurationChannels : this.playChannels;

        return negotiated.ContainsKey(channel) || this.clientChannels.Contains(channel);
    }

    /// <summary>
    /// Whether the connection may carry payloads on <paramref name="channel"/> in its current phase.
    /// </summary>
    public bool HasChannel(string channel) =>
        client.State.ToNetworkProtocol() is NetworkProtocol protocol && this.CanUse(channel, protocol);

    /// <summary>
    /// Queues <paramref name="payload"/> if the connection may carry its channel in its current phase.
    /// </summary>
    public async ValueTask<bool> SendAsync<TPayload>(TPayload payload) where TPayload : ICustomPayload<TPayload>
    {
        if (client.State.ToNetworkProtocol() is not NetworkProtocol protocol
            || !this.CanUse(TPayload.Channel, protocol))
        {
            return false;
        }

        using var buffer = new NetworkBuffer();
        payload.Write(buffer);

        var data = buffer.AsSpan(0, buffer.Size).ToArray();
        await client.QueuePacketAsync(CreatePacket(protocol, TPayload.Channel, data));

        return true;
    }

    /// <summary>
    /// Starts configuration the way NeoForge's server does: announce the handshake channels, ask a NeoForge client for
    /// its channels with an empty query, then ping. A NeoForge client answers the query before the ping, so by the
    /// pong (<see cref="FinishHandshakeAsync"/>) the server knows which kind of client it's talking to.
    /// </summary>
    public void StartConfiguration()
    {
        this.Send(ChannelPayloads.Unregister, ChannelPayloads.WriteChannelList([]));
        this.Send(ChannelPayloads.Register, ChannelPayloads.WriteChannelList(ChannelPayloads.Builtin));
        this.Send(ChannelPayloads.NeoForgeQuery, writer => ChannelPayloads.WriteQuery(writer, EmptyQuery));

        client.SendPacket(PingPacket.ClientboundConfiguration with { IdValue = HandshakePingId });
    }

    /// <summary>
    /// Handles the client's answer to a configuration ping. Answering the handshake ping finishes the handshake.
    /// </summary>
    /// <returns>Whether configuration should go on, which it does once, when the handshake succeeds.</returns>
    public async ValueTask<bool> FinishHandshakeAsync(int pongId)
    {
        if (pongId != HandshakePingId || this.handshakeFinished || !client.Connected)
            return false;

        this.handshakeFinished = true;

        if (this.IsNeoForge)
            return !this.refused;

        return await this.InitializeOtherConnectionAsync();
    }

    /// <summary>
    /// Updates the client's view of the server's channels once it entered play, like NeoForge: the configuration
    /// channels go away, and the server listens on the registration channels plus, for a client without NeoForge, its
    /// optional play channels.
    /// </summary>
    public async ValueTask FinishConfigurationAsync()
    {
        var configuration = ChannelPayloads.Builtin.Concat(this.configurationChannels.Keys);
        var unregister = ChannelPayloads.WriteChannelList(configuration);

        await client.QueuePacketAsync(CreatePacket(NetworkProtocol.Play, ChannelPayloads.Unregister, unregister));

        IEnumerable<string> play = this.IsNeoForge
            ? [ChannelPayloads.NeoForgeQuery]
            : this.GetOptionalListeningChannels(NetworkProtocol.Play);
        string[] listening = [ChannelPayloads.Register, ChannelPayloads.Unregister, .. play];
        var register = ChannelPayloads.WriteChannelList(listening);

        await client.QueuePacketAsync(CreatePacket(NetworkProtocol.Play, ChannelPayloads.Register, register));
    }

    /// <summary>
    /// Handles a payload the client sent: the brand, channel registrations, NeoForge's query, or a plugin's channel.
    /// Payloads on unknown channels are ignored, like vanilla does.
    /// </summary>
    public async ValueTask HandlePayloadAsync(string channel, ReadOnlyMemory<byte> data)
    {
        if (client.State.ToNetworkProtocol() is not NetworkProtocol protocol)
            return;

        switch (channel)
        {
            case ChannelPayloads.Brand:
                {
                    using var reader = new NetworkBuffer(data.ToArray());
                    client.Brand = reader.ReadString();
                    return;
                }
            case ChannelPayloads.Register:
                this.RegisterClientChannels(ChannelPayloads.ReadChannelList(data.Span));
                return;
            case ChannelPayloads.Unregister:
                {
                    var channels = ChannelPayloads.ReadChannelList(data.Span);
                    ImmutableInterlocked.Update(ref this.clientChannels, set => set.Except(channels));
                    return;
                }
            case ChannelPayloads.NeoForgeQuery when protocol == NetworkProtocol.Configuration:
                {
                    using var reader = new NetworkBuffer(data.ToArray());
                    await this.InitializeNeoForgeConnectionAsync(ChannelPayloads.ReadQuery(reader));
                    return;
                }
        }

        if (!registry.TryGet(channel, out var registration) || registration.Handler is null)
        {
            Log.UnknownChannel(client.Logger, channel);
            return;
        }

        if (!registration.Phase.Includes(protocol) || !this.CanUse(channel, protocol))
        {
            Log.ChannelNotSetUp(client.Logger, channel);
            return;
        }

        using var payloadReader = new NetworkBuffer(data.ToArray());
        var context = new PayloadContext
        {
            Client = client,
            Player = client.Player,
            Server = client.Server,
            Phase = protocol == NetworkProtocol.Configuration ? PayloadPhase.Configuration : PayloadPhase.Play
        };

        // Unloading the channel's plugin waits for running handlers, and refuses new ones.
        var plugin = registration.Owner as PluginContainer;
        if (plugin is not null && !plugin.TryEnter())
            return;

        try
        {
            await registration.Handler(payloadReader, context);
        }
        finally
        {
            plugin?.Exit();
        }
    }

    /// <summary>
    /// Agrees on channels with a NeoForge client's query, like NeoForge's server: on success the client learns the
    /// agreed channels; otherwise it learns why and is disconnected.
    /// </summary>
    private async ValueTask InitializeNeoForgeConnectionAsync(
        Dictionary<NetworkProtocol, List<NetworkComponent>> query)
    {
        this.IsNeoForge = true;

        var setup = new Dictionary<NetworkProtocol, IReadOnlyDictionary<string, string>>();
        var failures = new Dictionary<string, string>();

        foreach (var protocol in NegotiatedProtocols)
        {
            var serverComponents = registry.GetComponents(protocol);
            var clientComponents = query.GetValueOrDefault(protocol) ?? [];
            var result = NetworkComponentNegotiator.Negotiate(serverComponents, clientComponents);

            foreach (var (id, reason) in result.FailureReasons)
                failures[id] = reason;

            setup[protocol] = result.Channels;
        }

        if (failures.Count > 0)
        {
            this.refused = true;

            var channels = string.Join(", ", failures.Keys);
            Log.NegotiationFailed(client.Logger, channels);

            this.Send(
                ChannelPayloads.NeoForgeSetupFailed,
                writer => ChannelPayloads.WriteSetupFailed(writer, failures));

            var reason = new ChatMessage { Translate = "multiplayer.disconnect.incompatible" };
            await client.DisconnectAsync(reason.AddChatComponent("Obsidian"));
            return;
        }

        this.configurationChannels = setup[NetworkProtocol.Configuration].ToImmutableDictionary();
        this.playChannels = setup[NetworkProtocol.Play].ToImmutableDictionary();

        this.Send(ChannelPayloads.NeoForgeNetwork, writer => ChannelPayloads.WriteNetworkSetup(writer, setup));

        var listening = ChannelPayloads.Builtin.Concat(this.configurationChannels.Keys);
        this.Send(ChannelPayloads.Register, ChannelPayloads.WriteChannelList(listening));
    }

    /// <summary>
    /// Sets up a client without NeoForge, like NeoForge's server does: it can't have any channel, so the server's
    /// required channels refuse it. Otherwise the server announces its optional configuration channels, which the
    /// client may use once it registers them itself.
    /// </summary>
    private async ValueTask<bool> InitializeOtherConnectionAsync()
    {
        var missing = new List<string>();

        foreach (var protocol in NegotiatedProtocols)
        {
            var result = NetworkComponentNegotiator.Negotiate(registry.GetComponents(protocol), []);
            missing.AddRange(result.FailureReasons.Keys);
        }

        if (missing.Count > 0)
        {
            var channels = string.Join(", ", missing.Distinct());
            Log.NegotiationFailed(client.Logger, channels);

            var reason = $"This server needs client mods you don't have. Missing channels: {channels}";
            await client.DisconnectAsync(reason);
            return false;
        }

        var optional = this.GetOptionalListeningChannels(NetworkProtocol.Configuration);
        var listening = ChannelPayloads.Builtin.Concat(optional);
        this.Send(ChannelPayloads.Register, ChannelPayloads.WriteChannelList(listening));

        return true;
    }

    private void RegisterClientChannels(List<string> channels)
    {
        ImmutableInterlocked.Update(ref this.clientChannels, set => set.Union(channels));

        // Kept for plugins reading the server-wide set, which only grows; what a connection may use is tracked above.
        var registered = client.Server.RegisteredChannels;
        lock (registered)
            registered.UnionWith(channels);
    }

    /// <summary>
    /// The server's optional channels in <paramref name="protocol"/> it receives payloads on.
    /// </summary>
    private IEnumerable<string> GetOptionalListeningChannels(NetworkProtocol protocol) => registry
        .GetComponents(protocol)
        .Where(component => component.Optional && component.Flow != PacketFlow.Clientbound)
        .Select(component => component.Id);

    /// <summary>
    /// Sends a configuration payload right away, ahead of anything queued.
    /// </summary>
    private void Send(string channel, byte[] data) =>
        client.SendPacket(CreatePacket(NetworkProtocol.Configuration, channel, data));

    private void Send(string channel, Action<INetStreamWriter> write)
    {
        using var buffer = new NetworkBuffer();
        write(buffer);

        this.Send(channel, buffer.AsSpan(0, buffer.Size).ToArray());
    }

    private static CustomPayloadPacket CreatePacket(NetworkProtocol protocol, string channel, byte[] data)
    {
        var packet = protocol == NetworkProtocol.Configuration
            ? CustomPayloadPacket.ClientboundConfiguration
            : CustomPayloadPacket.ClientboundPlay;

        return packet with
        {
            Channel = channel,
            PluginData = data
        };
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Ignored a payload on unknown channel {Channel}")]
        public static partial void UnknownChannel(ILogger logger, string channel);

        [LoggerMessage(Level = LogLevel.Debug,
            Message = "Ignored a payload on channel {Channel}, which isn't set up for this connection and phase")]
        public static partial void ChannelNotSetUp(ILogger logger, string channel);

        [LoggerMessage(Level = LogLevel.Information,
            Message = "Refused a client whose payload channels don't match the server's: {Channels}")]
        public static partial void NegotiationFailed(ILogger logger, string channels);
    }
}
