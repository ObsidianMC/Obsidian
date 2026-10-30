using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading;
using Obsidian.API.Networking;
using Obsidian.Net.Packets.Common;
using Obsidian.Plugins;

namespace Obsidian.Net.Payloads;

/// <summary>
/// One connection's payload channels: the configuration handshake (NeoForge's, or for other clients the common channels
/// Fabric API speaks), the channels it agreed on, the channels the client registered itself, and receiving and sending
/// payloads on them.
/// </summary>
/// <remarks>
/// Only channel ids and versions are kept here, never plugin types, so the connection outlives a plugin reload. The
/// handshake runs on the connection's packet loop; the state it leaves behind may be read from any thread.
/// </remarks>
/// <param name="client">The connection.</param>
/// <param name="registry">The plugins' payload channels.</param>
/// <param name="moddedRegistries">
/// The registries plugins added to, which clients must agree on: a vanilla client can't join while there are any, and
/// NeoForge and Fabric clients get them with their loader's registry sync. None if null.
/// </param>
internal sealed partial class ClientChannels(IClient client, PayloadChannelRegistry registry,
    Func<IReadOnlyList<SyncedRegistry>>? moddedRegistries = null)
{
    private const int HandshakePingId = 0;

    // NeoForge's server asks with an empty query; the client answers with all of its channels.
    private static readonly ImmutableDictionary<NetworkProtocol, List<NetworkComponent>> EmptyQuery =
        ImmutableDictionary<NetworkProtocol, List<NetworkComponent>>.Empty;

    private static readonly ImmutableArray<NetworkProtocol> NegotiatedProtocols =
        [NetworkProtocol.Configuration, NetworkProtocol.Play];

    private ImmutableDictionary<string, string> configurationChannels = ImmutableDictionary<string, string>.Empty;
    private ImmutableDictionary<string, string> playChannels = ImmutableDictionary<string, string>.Empty;

    // The channels the client registered itself, per protocol: with minecraft:register in that protocol, or for play
    // with c:register during configuration.
    private ImmutableHashSet<string> configurationClientChannels = ImmutableHashSet<string>.Empty;
    private ImmutableHashSet<string> playClientChannels = ImmutableHashSet<string>.Empty;

    // How many channels a client may register itself, and how long their ids may be: the registrations are kept for
    // the whole connection, and the client chooses them.
    private const int MaxClientChannels = 1024;
    private const int MaxChannelLength = 256;

    private bool handshakeFinished;

    // Whether a client without NeoForge agreed on the common channels' version, so it tells the server its channels.
    private bool commonNetworking;

    // Whether a client without NeoForge passed the handshake's checks, so it may send on the server's channels.
    private bool otherAccepted;

    // What the handshake waits for the client to answer, and whether it's done, until the configuration handler goes
    // on with configuration.
    private Awaiting awaiting;
    private bool readyForConfiguration;

    // Set when the client sent a NeoForge query, whether or not it was accepted: a client that sent one but wasn't
    // accepted (refused, or a malformed query) mustn't go on as a vanilla client.
    private bool queryReceived;

    /// <summary>
    /// Whether the client answered with a NeoForge channel query that the server accepted, rather than being a vanilla
    /// (or other) client.
    /// </summary>
    public bool IsNeoForge { get; private set; }

    /// <summary>
    /// Whether the client agreed on channels with the server, through NeoForge's handshake or the common channels, so
    /// that a change of the server's channels concerns it.
    /// </summary>
    public bool IsModded => this.IsNeoForge || this.commonNetworking;

    /// <summary>
    /// Whether the connection may carry payloads on <paramref name="channel"/> in <paramref name="protocol"/>. Like
    /// NeoForge, vanilla and handshake channels always may; others must have been agreed on in the handshake or
    /// registered by the client with <c>minecraft:register</c> or <c>c:register</c>.
    /// </summary>
    /// <remarks>
    /// A channel the client registered itself has no version, so after a reload changes the channel's version it can
    /// only be told apart by its id: give a changed payload a new channel id rather than a new version.
    /// </remarks>
    public bool CanUse(string channel, NetworkProtocol protocol)
    {
        if (channel.StartsWith("minecraft:", StringComparison.Ordinal)
            || channel is ChannelPayloads.NeoForgeQuery
                or ChannelPayloads.NeoForgeNetwork
                or ChannelPayloads.NeoForgeSetupFailed)
        {
            return true;
        }

        if (channel is AttachmentPayload.Channel or MovementAbilitiesPayload.Channel)
            return protocol == NetworkProtocol.Play && this.HasNeoForgePlayChannel(channel);

        var negotiated = protocol == NetworkProtocol.Configuration ? this.configurationChannels : this.playChannels;

        // A reload can replace a channel with another version than the one agreed on; its payloads aren't compatible.
        if (negotiated.TryGetValue(channel, out var version))
            return registry.TryGet(channel, out var registration) && registration.Version == version;

        return this.RegisteredChannels(protocol).Contains(channel);
    }

    /// <summary>
    /// Whether a NeoForge client agreed on one of NeoForge's own play channels, which the server sends without a
    /// plugin's registration (see <see cref="ChannelPayloads.BuiltinPlayComponents"/>).
    /// </summary>
    public bool HasNeoForgePlayChannel(string channel) => this.IsNeoForge && this.playChannels.ContainsKey(channel);

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

        if (!this.queryReceived)
            return await this.InitializeOtherConnectionAsync();

        if (!this.IsNeoForge)
            return false;

        var registries = moddedRegistries?.Invoke() ?? [];
        if (registries.Count == 0)
            return true;

        return await this.StartRegistrySyncAsync(registries);
    }

    /// <summary>
    /// Whether the client applied the registries the server sent, so configuration can go on. True once.
    /// </summary>
    public bool TakeReadyForConfiguration()
    {
        var ready = this.readyForConfiguration;
        this.readyForConfiguration = false;

        return ready;
    }

    /// <summary>
    /// Sends a NeoForge client the registries plugins added to, like NeoForge's server does before vanilla's
    /// registries: their names, each registry's ids, then a completion that the client answers once it applied them.
    /// </summary>
    /// <returns>False: configuration goes on once the client answers.</returns>
    private async ValueTask<bool> StartRegistrySyncAsync(IReadOnlyList<SyncedRegistry> registries)
    {
        // A NeoForge client always has these channels; without them its ids for plugins' entries would be wrong.
        if (!this.configurationChannels.ContainsKey(ChannelPayloads.RegistrySyncStart)
            || !this.configurationChannels.ContainsKey(ChannelPayloads.RegistrySnapshot)
            || !this.configurationChannels.ContainsKey(ChannelPayloads.RegistrySyncCompleted))
        {
            await client.DisconnectAsync("Your client can't sync this server's registries.");
            return false;
        }

        this.awaiting = Awaiting.NeoForgeRegistrySync;

        this.Send(
            ChannelPayloads.RegistrySyncStart,
            writer => ChannelPayloads.WriteRegistrySyncStart(writer, registries));

        foreach (var snapshot in registries)
        {
            this.Send(
                ChannelPayloads.RegistrySnapshot,
                writer => ChannelPayloads.WriteRegistrySnapshot(writer, snapshot));
        }

        this.Send(ChannelPayloads.RegistrySyncCompleted, []);

        return false;
    }

    /// <summary>
    /// Updates the client's view of the server's channels once it entered play, like NeoForge: the configuration
    /// channels go away, and the server listens on the registration channels plus, for a client without NeoForge, its
    /// play channels.
    /// </summary>
    public async ValueTask FinishConfigurationAsync()
    {
        var configuration = ChannelPayloads.Builtin.Concat(this.configurationChannels.Keys);
        var unregister = ChannelPayloads.WriteChannelList(configuration);

        await client.QueuePacketAsync(CreatePacket(NetworkProtocol.Play, ChannelPayloads.Unregister, unregister));

        IEnumerable<string> play = this.IsNeoForge
            ? [ChannelPayloads.NeoForgeQuery]
            : this.GetListeningChannels(NetworkProtocol.Play);
        string[] listening = [ChannelPayloads.Register, ChannelPayloads.Unregister, .. play];
        var register = ChannelPayloads.WriteChannelList(listening);

        await client.QueuePacketAsync(CreatePacket(NetworkProtocol.Play, ChannelPayloads.Register, register));
    }

    /// <summary>
    /// Handles a payload the client sent: the brand, channel registrations, the handshake, or a plugin's channel.
    /// Payloads on unknown channels are ignored, like vanilla does.
    /// </summary>
    public async ValueTask HandlePayloadAsync(string channel, ReadOnlyMemory<byte> data)
    {
        // Packets read before a refusal disconnected the client mustn't carry on the handshake it failed.
        if (!client.Connected || client.State.ToNetworkProtocol() is not NetworkProtocol protocol)
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
                this.RegisterClientChannels(protocol, ChannelPayloads.ReadChannelList(data.Span));
                return;
            case ChannelPayloads.Unregister:
                {
                    var channels = ChannelPayloads.ReadChannelList(data.Span);
                    ImmutableInterlocked.Update(ref this.RegisteredChannels(protocol), set => set.Except(channels));
                    return;
                }
            case ChannelPayloads.NeoForgeQuery when protocol == NetworkProtocol.Configuration:
                await this.HandleQueryAsync(data);
                return;
            case ChannelPayloads.CommonVersion when this.awaiting == Awaiting.CommonVersion:
                await this.HandleCommonVersionAsync(data);
                return;
            case ChannelPayloads.CommonRegister when this.commonNetworking:
                await this.HandleCommonRegisterAsync(data);
                return;
            case ChannelPayloads.RegistrySyncCompleted when this.awaiting == Awaiting.NeoForgeRegistrySync:
            case ChannelPayloads.FabricRegistrySyncCompleted when this.awaiting == Awaiting.FabricRegistrySync:
                this.awaiting = Awaiting.Nothing;
                this.readyForConfiguration = true;
                return;
        }

        if (!registry.TryGet(channel, out var registration) || registration.Handler is null)
        {
            Log.UnknownChannel(client.Logger, channel);
            return;
        }

        // A NeoForge client may only send on channels it agreed on. Other clients register only the channels they
        // receive on, so once accepted they may send on any the server receives on: it announced those.
        var allowed = this.IsNeoForge ? this.CanUse(channel, protocol) : this.otherAccepted;

        if (!registration.Phase.Includes(protocol) || !allowed)
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

        // Releasing the channel's plugin waits for running handlers, and unloading refuses new ones.
        var plugin = registration.Owner as PluginContainer;
        using var call = plugin?.TryEnter();
        if (plugin is not null && call is null)
            return;

        await registration.Handler(payloadReader, context);
    }

    // A client gets one query, during the handshake; a malformed one disconnects it.
    private async ValueTask HandleQueryAsync(ReadOnlyMemory<byte> data)
    {
        if (this.queryReceived || this.handshakeFinished)
        {
            await client.DisconnectAsync("Unexpected NeoForge channel query.");
            return;
        }

        this.queryReceived = true;

        Dictionary<NetworkProtocol, List<NetworkComponent>> query;
        try
        {
            using var reader = new NetworkBuffer(data.ToArray());
            query = ChannelPayloads.ReadQuery(reader);

            if (query.Values.Any(components => components.DistinctBy(component => component.Id).Count()
                != components.Count))
            {
                throw new InvalidDataException("A channel is listed twice.");
            }
        }
        catch (Exception ex)
        {
            // Whatever the client sent, a query that can't be read refuses it rather than letting it continue.
            Log.MalformedPayload(client.Logger, ChannelPayloads.NeoForgeQuery, ex);
            await client.DisconnectAsync("Malformed NeoForge channel query.");
            return;
        }

        await this.InitializeNeoForgeConnectionAsync(query);
    }

    /// <summary>
    /// Agrees on channels with a NeoForge client's query, like NeoForge's server: on success the client learns the
    /// agreed channels; otherwise it learns why and is disconnected.
    /// </summary>
    private async ValueTask InitializeNeoForgeConnectionAsync(
        Dictionary<NetworkProtocol, List<NetworkComponent>> query)
    {
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
            var channels = string.Join(", ", failures.Keys);
            Log.NegotiationFailed(client.Logger, channels);

            this.Send(
                ChannelPayloads.NeoForgeSetupFailed,
                writer => ChannelPayloads.WriteSetupFailed(writer, failures));

            var reason = new ChatMessage { Translate = "multiplayer.disconnect.incompatible" };
            await client.DisconnectAsync(reason.AddChatComponent("Obsidian"));
            return;
        }

        this.IsNeoForge = true;
        this.configurationChannels = setup[NetworkProtocol.Configuration].ToImmutableDictionary();
        this.playChannels = setup[NetworkProtocol.Play].ToImmutableDictionary();

        this.Send(ChannelPayloads.NeoForgeNetwork, writer => ChannelPayloads.WriteNetworkSetup(writer, setup));

        var listening = ChannelPayloads.Builtin.Concat(this.configurationChannels.Keys);
        this.Send(ChannelPayloads.Register, ChannelPayloads.WriteChannelList(listening));
    }

    /// <summary>
    /// Sets up a client without NeoForge. One that registered the common channels (a Fabric client does, in answer to
    /// the server's <c>minecraft:register</c>) first agrees on their version and tells the server its play channels,
    /// like on a Fabric server; then, or right away for other clients, <see cref="CompleteOtherConnectionAsync"/>
    /// decides whether it may join.
    /// </summary>
    /// <returns>Whether configuration should go on now.</returns>
    private async ValueTask<bool> InitializeOtherConnectionAsync()
    {
        var registered = this.configurationClientChannels;

        if (!registered.Contains(ChannelPayloads.CommonVersion) || !registered.Contains(ChannelPayloads.CommonRegister))
            return await this.CompleteOtherConnectionAsync();

        this.awaiting = Awaiting.CommonVersion;
        this.Send(
            ChannelPayloads.CommonVersion,
            writer => ChannelPayloads.WriteCommonVersion(writer, [ChannelPayloads.CommonNetworkingVersion]));

        return false;
    }

    // The client's answer to the server's c:version: the versions it supports too.
    private async ValueTask HandleCommonVersionAsync(ReadOnlyMemory<byte> data)
    {
        List<int> versions;
        try
        {
            using var reader = new NetworkBuffer(data.ToArray());
            versions = ChannelPayloads.ReadCommonVersion(reader);
        }
        catch (Exception ex)
        {
            Log.MalformedPayload(client.Logger, ChannelPayloads.CommonVersion, ex);
            await client.DisconnectAsync("Malformed common network version.");
            return;
        }

        if (!versions.Contains(ChannelPayloads.CommonNetworkingVersion))
        {
            await client.DisconnectAsync(
                $"Unsupported common network version. Obsidian supports {ChannelPayloads.CommonNetworkingVersion}.");
            return;
        }

        this.commonNetworking = true;
        this.awaiting = Awaiting.CommonRegister;

        string[] play = [.. this.GetListeningChannels(NetworkProtocol.Play)];
        this.Send(
            ChannelPayloads.CommonRegister,
            writer => ChannelPayloads.WriteCommonRegister(writer, NetworkProtocol.Play, play));
    }

    // The client's channels for a phase, which replace those it registered for it before. During the handshake, its
    // play channels answer the server's.
    private async ValueTask HandleCommonRegisterAsync(ReadOnlyMemory<byte> data)
    {
        int version;
        NetworkProtocol? protocol;
        List<string> channels;
        try
        {
            using var reader = new NetworkBuffer(data.ToArray());
            (version, protocol, channels) = ChannelPayloads.ReadCommonRegister(reader);
        }
        catch (Exception ex)
        {
            Log.MalformedPayload(client.Logger, ChannelPayloads.CommonRegister, ex);
            await client.DisconnectAsync("Malformed common channel registration.");
            return;
        }

        if (version != ChannelPayloads.CommonNetworkingVersion)
        {
            await client.DisconnectAsync("Common channels registered with a version that wasn't agreed on.");
            return;
        }

        if (protocol is not NetworkProtocol phase)
            return;

        var valid = ValidChannels(channels).ToImmutableHashSet();
        if (valid.Count <= MaxClientChannels)
            Volatile.Write(ref this.RegisteredChannels(phase), valid);

        if (this.awaiting == Awaiting.CommonRegister && phase == NetworkProtocol.Play)
        {
            this.awaiting = Awaiting.Nothing;
            this.readyForConfiguration = await this.CompleteOtherConnectionAsync();
        }
    }

    /// <summary>
    /// Decides whether a client without NeoForge may join. A vanilla client can't have any channel, so the server's
    /// required channels refuse it, like on NeoForge's server. A client that told the server its channels with the
    /// common channels must have registered each required channel it receives payloads on; those only the server
    /// receives on can't be checked, since a client only registers the channels it receives on. Content plugins added
    /// needs a client with Fabric API's registry sync, which it gets before configuration goes on.
    /// </summary>
    /// <returns>Whether configuration should go on now.</returns>
    private async ValueTask<bool> CompleteOtherConnectionAsync()
    {
        // Without a registry sync, the client has no way to learn plugins' items, so it couldn't show them.
        var registries = moddedRegistries?.Invoke() ?? [];
        if (registries.Count > 0 && !this.configurationClientChannels.Contains(ChannelPayloads.FabricRegistrySync))
        {
            Log.ModdedContentRefused(client.Logger);
            await client.DisconnectAsync(
                "This server has content from its plugins that needs NeoForge or Fabric with the matching mods.");
            return false;
        }

        var missing = NegotiatedProtocols
            .SelectMany(this.FindMissingChannels)
            .Distinct()
            .ToList();

        if (missing.Count > 0)
        {
            var channels = string.Join(", ", missing);
            Log.NegotiationFailed(client.Logger, channels);

            var reason = $"This server needs client mods you don't have. Missing channels: {channels}";
            await client.DisconnectAsync(reason);
            return false;
        }

        this.otherAccepted = true;

        var listening = ChannelPayloads.Builtin.Concat(this.GetListeningChannels(NetworkProtocol.Configuration));
        this.Send(ChannelPayloads.Register, ChannelPayloads.WriteChannelList(listening));

        if (registries.Count == 0)
            return true;

        this.awaiting = Awaiting.FabricRegistrySync;
        this.Send(
            ChannelPayloads.FabricRegistrySync,
            writer => ChannelPayloads.WriteFabricRegistrySync(writer, registries));

        return false;
    }

    // The server's required channels in a protocol that a client without NeoForge lacks; see
    // CompleteOtherConnectionAsync.
    private IEnumerable<string> FindMissingChannels(NetworkProtocol protocol) => registry
        .GetComponents(protocol)
        .Where(component => !component.Optional)
        .Where(component => !this.commonNetworking
            || (component.Flow != PacketFlow.Serverbound
                && !this.RegisteredChannels(protocol).Contains(component.Id)))
        .Select(component => component.Id);

    private void RegisterClientChannels(NetworkProtocol protocol, List<string> channels)
    {
        var valid = ValidChannels(channels);

        ImmutableInterlocked.Update(ref this.RegisteredChannels(protocol), set =>
        {
            var registered = set.Union(valid);
            return registered.Count <= MaxClientChannels ? registered : set;
        });
    }

    private static IEnumerable<string> ValidChannels(IEnumerable<string> channels) =>
        channels.Where(channel => channel.Length <= MaxChannelLength && channel.Contains(':'));

    // The channels the client registered itself for a protocol.
    private ref ImmutableHashSet<string> RegisteredChannels(NetworkProtocol protocol)
    {
        if (protocol == NetworkProtocol.Configuration)
            return ref this.configurationClientChannels;

        return ref this.playClientChannels;
    }

    /// <summary>
    /// The server's channels in <paramref name="protocol"/> it receives payloads on.
    /// </summary>
    private IEnumerable<string> GetListeningChannels(NetworkProtocol protocol) => registry
        .GetComponents(protocol)
        .Where(component => component.Flow != PacketFlow.Clientbound)
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

    internal static CustomPayloadPacket CreatePacket(NetworkProtocol protocol, string channel, byte[] data)
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

    private enum Awaiting
    {
        Nothing,
        CommonVersion,
        CommonRegister,
        NeoForgeRegistrySync,
        FabricRegistrySync
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Ignored a payload on unknown channel {Channel}")]
        public static partial void UnknownChannel(ILogger logger, string channel);

        [LoggerMessage(Level = LogLevel.Debug,
            Message = "Ignored a payload on channel {Channel}, which isn't set up for this connection and phase")]
        public static partial void ChannelNotSetUp(ILogger logger, string channel);

        [LoggerMessage(Level = LogLevel.Information,
            Message = "Refused a client without NeoForge or Fabric API: plugins added content it can't show")]
        public static partial void ModdedContentRefused(ILogger logger);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Disconnecting a client that sent a malformed {Channel} payload")]
        public static partial void MalformedPayload(ILogger logger, string channel, Exception exception);

        [LoggerMessage(Level = LogLevel.Information,
            Message = "Refused a client whose payload channels don't match the server's: {Channels}")]
        public static partial void NegotiationFailed(ILogger logger, string channels);
    }
}
