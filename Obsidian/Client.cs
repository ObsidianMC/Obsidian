using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Obsidian.API.Events;
using Obsidian.API.Networking;
using Obsidian.Entities;
using Obsidian.Events.EventArgs;
using Obsidian.Integrated;
using Obsidian.Net;
using Obsidian.Net.ClientHandlers;
using Obsidian.Net.Packets.Common;
using Obsidian.Net.Packets.Login.Clientbound;
using Obsidian.Net.Payloads;
using Obsidian.Services;
using Obsidian.Utilities.Mojang;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;

namespace Obsidian;

public sealed partial class Client : IClient
{
    private const int MaxBufferSize = 1024 * 8;

    /// <summary>
    /// How many <see cref="KeepAlivePacket"/>s the client has missed.
    /// </summary>
    public long? LastKeepAliveId { get; set; }

    /// <summary>
    /// The public key/signature data received from mojang.
    /// </summary>
    public SignatureData? SignatureData { get; set; }

    /// <summary>
    /// Used for signing chat messages.
    /// </summary>
    internal SignedMessage? messageSigningData;

    private readonly IEventDispatcher eventDispatcher;
    private readonly IUserCache userCache;
    private readonly ServerMetrics serverMetrics;
    private readonly IServiceProvider serviceProvider;
    private readonly ObjectPool<SocketAsyncEventArgs> pool;

    /// <summary>
    /// Whether this client is disposed.
    /// </summary>
    private bool disposed;

    /// <summary>
    /// The random token used to encrypt the stream; empty until the encryption request is sent.
    /// </summary>
    public ReadOnlyMemory<byte> RandomToken { get; private set; }

    /// <summary>
    /// The server's token used to encrypt the stream.
    /// </summary>
    private byte[]? sharedKey;

    /// <summary>
    /// The mojang user that the client and player is associated with.
    /// </summary>
    private CachedProfile? profile;

    private readonly Channel<IClientboundPacket> packetQueue;

    /// <summary>
    /// The cancellation token source used to cancel the packet queue loop and disconnect the client.
    /// </summary>
    private readonly CancellationTokenSource cancellationSource = new();

    // The player's leave, once it was raised; see LeaveAsync.
    private TaskCompletionSource? leaving;

    // Set once Disconnect ran; it may be called again (a shutdown disconnecting a client that already left). Guarded by
    // lifecycleLock together with disposing and registering the client, so a login that resumes after a disconnect
    // doesn't register it again.
    private bool disconnected;
    private readonly Lock lifecycleLock = new();

    // The client whose leave event is being raised on this flow, so a leave handler that disconnects the same player
    // doesn't wait for the leave it's part of.
    private static readonly AsyncLocal<Client?> raisingLeave = new();

    /// <summary>
    /// Used to handle packets while the client is in a <see cref="ClientState.Play"/> state.
    /// </summary>
    private readonly FrozenDictionary<ClientState, ClientHandler> handlers;

    /// <summary>
    /// Used to continuously send and receive encrypted packets from the client.
    /// </summary>
    private readonly PacketCryptography packetCryptography;

    private readonly ILoggerFactory loggerFactory;

    private string? ServerId => sharedKey?.Concat(packetCryptography.PublicKey).MinecraftShaDigest();

    /// <summary>
    /// The player's entity id.
    /// </summary>
    public int Id { get; private set; }

    /// <summary>
    /// The client's ping in milliseconds.
    /// </summary>
    public int Ping { get; set; }

    /// <summary>
    /// Whether the client has compression enabled on the Minecraft stream.
    /// </summary>
    public bool CompressionEnabled => this.compressionThreshold >= 0;

    // The size from which packets are compressed, or -1 before compression is enabled (see EnableCompression).
    private volatile int compressionThreshold = -1;

    /// <summary>
    /// Whether the stream has encryption enabled. This can be set to false when the client is connecting through LAN or when the server is in offline mode.
    /// </summary>
    public bool EncryptionEnabled { get; private set; }

    /// <summary>
    /// Which state of the protocol the client is currently in.
    /// </summary>
    public ClientState State { get; private set; } = ClientState.Handshaking;

    /// <summary>
    /// The client's ip and port used to establish this connection.
    /// </summary>
    public IPEndPoint? RemoteEndPoint => this.Socket.RemoteEndPoint as IPEndPoint;

    public string? Ip => this.RemoteEndPoint?.Address.ToString();

    /// <summary>
    /// Executed when the client disconnects.
    /// </summary>
    public event Action<Client>? Disconnected;

    /// <summary>
    /// Used to log actions caused by the client.
    /// </summary>
    public ILogger Logger { get; private set; }

    public IPlayer? Player { get; private set; }

    public IServer Server { get; }

    public string? Brand { get; set; }

    public bool Connected => this.Socket.Connected;

    /// <summary>
    /// The connection's payload channels and NeoForge handshake.
    /// </summary>
    internal ClientChannels Channels { get; private set; }

    /// <summary>
    /// The server's content (see <see cref="Server.GetContentVersion"/>) when the client last finished configuration:
    /// its registries and channels are those.
    /// </summary>
    internal ContentVersion? ConfiguredContent { get; set; }

    /// <summary>
    /// Whether the server asked the client to go back to configuration and waits for it to acknowledge: the player has
    /// left the world, and the client's play packets are ignored meanwhile.
    /// </summary>
    internal bool AwaitingReconfiguration { get; private set; }

    private volatile bool reconfigurationRequested;

    public Client(IEventDispatcher eventDispatcher, IServer server, ILoggerFactory loggerFactory,
        IUserCache playerCache,
        ServerMetrics serverMetrics, IServiceProvider serviceProvider, ObjectPool<SocketAsyncEventArgs> pool)
    {
        this.eventDispatcher = eventDispatcher;
        this.Server = server;
        this.loggerFactory = loggerFactory;
        this.userCache = playerCache;
        this.serverMetrics = serverMetrics;
        this.serviceProvider = serviceProvider;
        this.pool = pool;
        this.Logger = loggerFactory.CreateLogger("ConnectionHandler");

        packetCryptography = new();
        this.handlers = new Dictionary<ClientState, ClientHandler>()
        {
            { ClientState.Login, new LoginClientHandler { Client = this } },
            { ClientState.Configuration, new ConfigurationClientHandler { Client = this } },
            { ClientState.Play, new PlayClientHandler { Client = this } }
        }.ToFrozenDictionary();

        var concreteServer = (Server)server;
        this.Channels = new ClientChannels(this, concreteServer.PayloadChannels, concreteServer.GetModdedRegistries);

        packetQueue = Channel.CreateUnbounded<IClientboundPacket>(new() { SingleReader = true, SingleWriter = false });
    }

    public async ValueTask<bool> TrySetCachedProfileAsync(string username)
    {
        ArgumentNullException.ThrowIfNull(username, nameof(username));

        this.profile = await this.userCache.GetCachedUserFromNameAsync(username);

        if (this.profile is null)
        {
            await DisconnectAsync("Account not found in the Mojang database");

            return false;
        }
        else if (this.Server.Configuration.Whitelist && !this.Server.IsWhitelisted(this.profile.Uuid))
        {
            await DisconnectAsync("You are not whitelisted on this server\nContact server administrator");

            return false;
        }

        this.InitializeId();

        return true;
    }

    public ReadOnlySpan<byte> SetSharedKeyAndDecodeVerifyToken(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> verifyToken)
    {
        this.sharedKey = packetCryptography.Decrypt(secret);
        return this.packetCryptography.Decrypt(verifyToken);
    }

    public void Initialize(IWorld world)
    {
        if (this.profile is null)
            throw new UnreachableException("Profile was not set or is null.");

        this.Player = this.CreatePlayer(this.profile.Uuid, this.profile.Name, world);

        this.packetCryptography.GenerateKeyPair();

        var (publicKey, randomToken) = this.packetCryptography.GeneratePublicKeyAndToken();

        this.RandomToken = randomToken;

        this.SendPacket(new HelloPacket
        {
            PublicKey = publicKey,
            VerifyToken = randomToken,
            ShouldAuthenticate = true//I don't know how we're supposed to use this
        });
    }

    /// <summary>
    /// Logs the player in without Mojang authentication, with the UUID offline servers derive from the name, or with
    /// <paramref name="uuid"/> (an integrated server's local player).
    /// </summary>
    public void InitializeOffline(string username, IWorld world, Guid? uuid = null)
    {
        this.InitializeId();

        this.Player = this.CreatePlayer(uuid ?? GuidHelper.FromStringHash($"OfflinePlayer:{username}"), username, world);

        this.EnableCompression();
        this.SendPacket(new LoginFinishedPacket(Player.Uuid, Player.Username)
        {
            SkinProperties = this.Player.SkinProperties,
        });
    }

    public async ValueTask DisconnectAsync(ChatMessage reason)
    {
        await this.LeaveAsync();

        if (this.State == ClientState.Login)
        {
            await this.QueuePacketAsync(new LoginDisconnectPacket { ReasonJson = reason.ToString(Globals.JsonOptions) });
        }
        else
        {
            var packet = this.State == ClientState.Play ? DisconnectPacket.ClientboundPlay with { Reason = reason }
               : DisconnectPacket.ClientboundConfiguration with { Reason = reason };

            await this.QueuePacketAsync(packet);
        }

        this.Disconnect();
    }

    public async ValueTask QueuePacketAsync(IClientboundPacket packet)
    {
        if (this.disposed
            || this.cancellationSource.IsCancellationRequested
            || !this.Connected
            || !this.Accepts(packet))
        {
            return;
        }

        try
        {
            var args = new QueuePacketEventArgs(this.Server, this, packet);
            var result = await this.eventDispatcher.ExecuteEventAsync(args);
            if (result == EventResult.Cancelled)
            {
                Logger.LogDebug("Packet {PacketId} was sent to the queue, however an event handler has cancelled it.", args.Packet.Id);
                return;
            }

            if (this.disposed || this.cancellationSource.IsCancellationRequested)
                return;
            this.packetQueue.Writer.TryWrite(packet);
        }
        catch (OperationCanceledException) when (this.cancellationSource.IsCancellationRequested)
        {
            // A disconnect cancels this client's send, not the world tick awaiting it.
        }
    }

    public bool SendPacket(IClientboundPacket packet) => this.SendAsync(packet);

    // While the client goes back to configuration, nothing but the switch reaches it, and once it's there, play
    // packets still sent for the player who left (a chunk send under way, say) don't either: it would misread them.
    // Sending checks it under the send lock, which the switches change the state under, so no play packet is written
    // after the switch.
    private bool Accepts(IClientboundPacket packet)
    {
        if (packet is Net.Packets.Play.Clientbound.StartConfigurationPacket)
            return true;

        if (this.AwaitingReconfiguration)
            return false;

        var playNamespace = typeof(Net.Packets.Play.Clientbound.StartConfigurationPacket).Namespace;
        var play = packet is Net.Packets.CommonPacket common
            ? common.State == ClientState.Play
            : packet.GetType().Namespace == playNamespace;

        return !(play && this.State == ClientState.Configuration);
    }

    public bool HasChannel(string channel) => this.Channels.HasChannel(channel);

    public ValueTask<bool> SendPayloadAsync<TPayload>(TPayload payload) where TPayload : ICustomPayload<TPayload> =>
        this.Channels.SendAsync(payload);

    /// <summary>
    /// Tells the client to compress packets from the configured size (vanilla's network compression threshold; a
    /// negative one leaves compression off) and frames every later packet in both directions that way. Like vanilla, it's
    /// sent right before the login finishes.
    /// </summary>
    private void EnableCompression()
    {
        var threshold = this.Server.Configuration.Network.CompressionThreshold;
        if (threshold < 0 || this.CompressionEnabled)
            return;

        // The packet itself still goes out uncompressed. Nothing else is sent or received during login meanwhile.
        this.SendPacket(new LoginCompressionPacket(threshold));
        this.compressionThreshold = threshold;
    }

    internal void Login(MojangProfile user)
    {
        this.Player!.SkinProperties = user.Properties!;
        this.EncryptionEnabled = true;
        this.loginPending = true;
    }

    internal void ThrowIfInvalidEncryptionRequest()
    {
        if (this.Player is null)
            throw new InvalidOperationException("Received Encryption Response before sending Login Start.");

        if (this.RandomToken.IsEmpty)
            throw new InvalidOperationException("Received Encryption Response before sending Encryption Request.");
    }

    public void SetState(ClientState state) => this.State = state;

    public void Dispose()
    {
        lock (this.lifecycleLock)
        {
            if (this.disposed)
                return;

            this.disposed = true;

            try
            {
                this.cancellationSource.Cancel();
                this.cancellationSource.Dispose();
                this.Socket.Dispose();
            }
            catch (ObjectDisposedException) { }
        }

        GC.SuppressFinalize(this);
    }

    public async ValueTask<bool> VerifyProfileAsync()
    {
        if (await this.HasJoinedAsync() is not MojangProfile user)
        {
            Log.AuthenticationFailed(this.Logger, this.Player?.Username);
            await this.DisconnectAsync("Unable to authenticate...");
            return false;
        }

        this.Login(user);

        return true;
    }

    public void Disconnect()
    {
        lock (this.lifecycleLock)
        {
            if (this.disposed || this.disconnected)
                return;

            this.disconnected = true;
            this.cancellationSource.Cancel();
        }

        Disconnected?.Invoke(this);

        // A player whose client closed the connection (as vanilla's clients do to quit) leaves too and is saved, and
        // other players stop seeing them. A player still logging in or configuring hasn't joined, so there's nothing to
        // leave.
        if (this.State == ClientState.Play)
            _ = this.LeaveAsync();

        this.receiveEvent.Completed -= this.OnAsyncCompleted;
        this.sendEvent.Completed -= this.OnAsyncCompleted;

        this.pool.Return(this.receiveEvent);
        this.pool.Return(this.sendEvent);

        var removed = this.Server.Connections.Remove(this.Id, out _);

        if (this.Player != null)
            this.Server.RemovePlayer(this.Player);

        try
        {
            this.Socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }

        this.Socket.Close();

        this.receiving = false;
        this.sending = false;

        lock (this.sendLock)
        {
            this.sendBufferMain.Clear();
            this.sendBufferFlush.Clear();

            this.sendBufferFlushOffset = 0;
        }

        this.Dispose();
    }

    private async Task<MojangProfile?> HasJoinedAsync() => await this.userCache.HasJoinedAsync(this.Player!.Username, this.ServerId!);

    private async Task HandlePacketQueueAsync()
    {
        try
        {
            var cancellationToken = this.cancellationSource.Token;
            while (this.Connected && !this.disposed && !cancellationToken.IsCancellationRequested)
            {
                var packet = await this.packetQueue.Reader.ReadAsync(cancellationToken);

                if (packet is null)
                    continue;

                this.SendPacket(packet);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
        {
            // The client disconnected.
        }
    }


    private async ValueTask<bool> HandlePacketAsync(PacketData packetData)
    {
        // Between packets nothing else of this client's is running, so the player can leave the world unchanged.
        if (this.reconfigurationRequested && this.State == ClientState.Play && !this.AwaitingReconfiguration)
        {
            this.reconfigurationRequested = false;
            await this.ReconfigureAsync();
        }

        // Until it acknowledges going back to configuration, the client's play packets are for a player who left.
        if (this.AwaitingReconfiguration && packetData.Id != ConfigurationAcknowledgedId)
            return true;

        try
        {
            return await this.handlers[this.State].HandleAsync(packetData);
        }
        catch (Exception ex)
        {
            Log.PacketHandlingFailed(this.Logger, ex, packetData.Id, this.State);
        }

        return false;
    }

    private void InitializeId()
    {
        lock (this.lifecycleLock)
        {
            // Login awaits, and the client may have disconnected meanwhile; it stays out of the connections then.
            if (this.disconnected)
                return;

            this.Server.Connections.Remove(this.Id, out _);

            this.Id = Obsidian.Server.GetNextEntityId();

            this.Server.Connections.TryAdd(this.Id, this);

            this.Logger = this.loggerFactory.CreateLogger($"Client({this.Id})");
        }
    }

    // The play packet that acknowledges going back to configuration.
    private const int ConfigurationAcknowledgedId = 15;

    /// <summary>
    /// Has the player go back through configuration, like vanilla's reconfiguration, so that the client learns the
    /// server's current content: they leave the world (saved, like leaving), the channel handshake and registry sync
    /// run again, and they join again. A client that can't have the content is refused, like when joining. It happens
    /// before the client's next packet is handled, when nothing of the client's is running.
    /// </summary>
    internal void RequestReconfiguration() => this.reconfigurationRequested = true;

    /// <summary>
    /// Disconnects a client that hasn't acknowledged going back to configuration within the keep-alive timeout, as it
    /// isn't sent keep-alives meanwhile.
    /// </summary>
    internal ValueTask CheckReconfigurationTimeoutAsync()
    {
        var waited = Environment.TickCount64 - this.reconfigurationStarted;
        if (waited > this.Server.Configuration.Network.KeepAliveTimeoutInterval)
            return this.DisconnectAsync("Timed out..");

        return ValueTask.CompletedTask;
    }

    // When the server sent the client back to configuration.
    private long reconfigurationStarted;

    /// <summary>
    /// Sends a keep-alive and records it as unanswered, under the send lock, so that going back to configuration,
    /// which forgets it, can't come in between.
    /// </summary>
    internal void SendKeepAlive(KeepAlivePacket packet)
    {
        lock (this.sendLock)
        {
            if (this.SendPacket(packet))
                this.LastKeepAliveId = packet.KeepAliveId;
        }
    }

    private async ValueTask ReconfigureAsync()
    {
        if (this.Player is not Player player)
            return;

        // Like vanilla, the player leaves the world before the client switches, and nothing else is sent to them in
        // play from here.
        lock (this.sendLock)
        {
            this.AwaitingReconfiguration = true;
            this.LastKeepAliveId = null;
            this.reconfigurationStarted = Environment.TickCount64;
        }

        this.Server.RemovePlayer(player);
        await player.StopSendingChunksAsync();

        // The connection's one leave, until the player joins again.
        await this.LeaveAsync();
        await this.QueuePacketAsync(new Net.Packets.Play.Clientbound.StartConfigurationPacket());
    }

    /// <summary>
    /// Starts the configuration handshake, recording the server's content first: what the handshake agrees on is at
    /// least as new, so a later change is told apart.
    /// </summary>
    internal void StartConfiguration()
    {
        this.ConfiguredContent = ((Server)this.Server).GetContentVersion();
        this.Channels.StartConfiguration();
    }

    /// <summary>
    /// Whether the client, configured with <see cref="ConfiguredContent"/>, has to go back through configuration for
    /// <paramref name="current"/>: other registries, or other channels for a client that agreed on them. Other tags
    /// are sent in play instead.
    /// </summary>
    internal bool NeedsReconfiguration(ContentVersion current) =>
        this.ConfiguredContent is ContentVersion configured
        && (configured.Registries != current.Registries
            || this.Channels.IsModded && configured.Channels != current.Channels);

    /// <summary>
    /// Goes back to configuration once the client acknowledged it, with a new player for the new join.
    /// </summary>
    internal async ValueTask AcknowledgeReconfigurationAsync()
    {
        if (!this.AwaitingReconfiguration || this.Player is not Player player)
        {
            await this.DisconnectAsync("Unexpected configuration acknowledgement.");
            return;
        }

        lock (this.sendLock)
        {
            this.AwaitingReconfiguration = false;
            this.LastKeepAliveId = null;
            this.SetState(ClientState.Configuration);
        }

        // The new join loads the player as they were saved when they left, and they leave again on their own: the
        // earlier leave finished before the switch.
        this.Player = this.CreatePlayer(player.Uuid, player.Username, this.Server.DefaultWorld);
        this.Player.SkinProperties = player.SkinProperties;
        this.leaving = null;

        var concreteServer = (Server)this.Server;
        this.Channels = new ClientChannels(this, concreteServer.PayloadChannels, concreteServer.GetModdedRegistries);
        ((ConfigurationClientHandler)this.handlers[ClientState.Configuration]).Reset();

        this.StartConfiguration();
    }

    private Player CreatePlayer(Guid uuid, string username, IWorld world) => new(uuid, username, this, world)
    {
        Server = this.serviceProvider.GetRequiredService<IServer>(),

        // Vanilla's isSingleplayerOwner: an integrated server's local player.
        IsSingleplayerOwner = this.Server is Server { Integrated: IntegratedSession integrated }
            && uuid == integrated.Configuration.LocalPlayerUuid
            && integrated.IsLocalPlayer(username)
    };

    /// <summary>
    /// Raises the player's leave event, which saves them, once however the connection ends: closed by the server
    /// (<see cref="DisconnectAsync"/>) or by the client (<see cref="Disconnect"/>). Every call returns the same leave, so
    /// a later one waits for it, and the server's shutdown waits for it too (<see cref="Server.TrackLeave"/>). A call from
    /// a leave handler itself (kicking the player who's leaving) or from one of the player's ticks doesn't wait, since
    /// the leave waits for those.
    /// </summary>
    private Task LeaveAsync()
    {
        if (this.Player is not Player player || raisingLeave.Value == this)
            return Task.CompletedTask;

        // The leave waits for a tick of the player under way, so a leave from inside one isn't waited for.
        var inOwnTick = player.IsInOwnTick;

        var leave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Interlocked.CompareExchange(ref this.leaving, leave, null) is TaskCompletionSource started)
            return inOwnTick ? Task.CompletedTask : started.Task;

        // Tracked before it starts, so a shutdown that begins meanwhile still waits for it.
        if (this.Server is Server server)
            server.TrackLeave(leave.Task);

        // Raised apart from the caller's flow, so that it doesn't count as being in the caller's tick.
        using (ExecutionContext.SuppressFlow())
            _ = Task.Run(() => this.RaiseLeaveAsync(player, leave));

        return inOwnTick ? Task.CompletedTask : leave.Task;
    }

    // Raises the leave event and completes the leave; it never throws.
    private async Task RaiseLeaveAsync(Player player, TaskCompletionSource leave)
    {
        raisingLeave.Value = this;
        try
        {
            EventResult result;
            try
            {
                // The player's ticks stop first, so the leave saves them as the last tick left them.
                await player.StopTickingAsync();

                result = await this.eventDispatcher.ExecuteEventAsync(new PlayerLeaveEventArgs(player, this.Server, DateTimeOffset.Now));
            }
            catch (Exception ex)
            {
                Log.LeaveFailed(this.Logger, ex, player.Username);
                result = EventResult.Failed;
            }

            // Leaving saves the player, so a failed leave may have lost their data; it's reported like a failed save.
            if (result == EventResult.Failed && this.Server is Server server)
                server.ReportPlayerSaveFailed(player.Username);
        }
        finally
        {
            leave.SetResult();
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Handling {Username} leaving failed")]
        public static partial void LeaveFailed(ILogger logger, Exception exception, string username);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to authenticate {Username}")]
        public static partial void AuthenticationFailed(ILogger logger, string? username);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Handling packet {PacketId} in state {State} failed")]
        public static partial void PacketHandlingFailed(ILogger logger, Exception exception, int packetId, ClientState state);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Disconnecting client after socket error {SocketError}")]
        public static partial void SocketFailed(ILogger logger, SocketError socketError);
    }
}
