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

    // A full distance-32 view is 3,725 chunks: at most 11,176 entries with one chunk per batch and its center.
    private const int MaximumOutgoingEntries = 65_536;
    private static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(3);

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
    private volatile bool disposed;

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

    /// <summary>
    /// The client's outgoing pipeline: every packet queued to it, in order. Its one reader runs plugins' queue handlers
    /// for each packet and then sends it, so that queueing a packet never waits for plugins' code (see
    /// <see cref="QueuePacketAsync"/>).
    /// </summary>
    private readonly Channel<Outgoing> packetQueue;

    // Entries queued and not yet sent or dropped. A direct send waits behind them (see SendPacket).
    private int unsent;

    private readonly Task packetQueueTask;
    private readonly CancellationToken cancellationToken;

    // Guarded by sendLock: no new entries or direct sends follow the terminal entry.
    private volatile bool disconnectRequested;

    /// <summary>
    /// The cancellation token source used to cancel the packet queue loop and disconnect the client.
    /// </summary>
    private readonly CancellationTokenSource cancellationSource = new();

    // The player's leave, once it was raised; see LeaveAsync.
    private TaskCompletionSource? leaving;

    // Starting a leave and tracking it happen together, so that no caller sees a leave the server doesn't track yet.
    private readonly Lock leavingLock = new();

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

    /// <summary>
    /// The server, or a plugin's own instance of it during the plugin's calls (see <see cref="IPluginScopedServer"/>).
    /// </summary>
    public IServer Server => ServerScope.Resolve(this.HostServer);

    /// <summary>
    /// The server itself, whoever's code runs: the one the connection's own code uses.
    /// </summary>
    internal IServer HostServer { get; }

    public string? Brand { get; set; }

    // No socket yet: the pipeline starts with the client, before it connects.
    public bool Connected => this.Socket?.Connected == true;

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
    /// How many plugins' menu types there were when the client last started configuration: it was sent at least those
    /// (see <see cref="Player.OpenInventoryAsync"/>).
    /// </summary>
    internal int ConfiguredPluginMenus { get; private set; }

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
        this.HostServer = server;
        this.loggerFactory = loggerFactory;
        this.userCache = playerCache;
        this.serverMetrics = serverMetrics;
        this.serviceProvider = serviceProvider;
        this.pool = pool;
        this.Logger = loggerFactory.CreateLogger("ConnectionHandler");
        this.flushOutput = this.FlushOutput;

        packetCryptography = new();
        this.handlers = new Dictionary<ClientState, ClientHandler>()
        {
            { ClientState.Login, new LoginClientHandler { Client = this } },
            { ClientState.Configuration, new ConfigurationClientHandler { Client = this } },
            { ClientState.Play, new PlayClientHandler { Client = this } }
        }.ToFrozenDictionary();

        var concreteServer = (Server)server;
        this.Channels = new ClientChannels(this, concreteServer.PayloadChannels, concreteServer.GetModdedRegistries);

        this.packetQueue = Channel.CreateUnbounded<Outgoing>(new()
        {
            // Teardown can drain abandoned entries while the consumer is inside a plugin.
            SingleReader = false,
            SingleWriter = false
        });

        // Plugins' handlers run in the pipeline's own flow, which inherits nothing from whoever created the client.
        this.cancellationToken = this.cancellationSource.Token;
        using (ExecutionContext.SuppressFlow())
            this.packetQueueTask = Task.Run(this.HandlePacketQueueAsync);
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
        else if (this.HostServer is Server { Bans: var bans } && bans.GetBan(this.profile.Uuid, this.profile.Name) is BannedPlayer ban)
        {
            await DisconnectAsync(BanList.DisconnectMessage(ban.Reason));

            return false;
        }
        // Players whitelisted by name before their UUID was known match by name.
        else if (this.HostServer.Configuration.Whitelist && !this.HostServer.IsWhitelisted(this.profile.Uuid)
            && !this.HostServer.IsWhitelisted(this.profile.Name))
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

    /// <summary>
    /// Queues the final reason and starts leaving, without waiting for outgoing handlers or socket output.
    /// The pipeline closes after output drains; a deadline closes it if a handler or socket stalls.
    /// </summary>
    public ValueTask DisconnectAsync(ChatMessage reason)
    {
        bool startDeadline;
        lock (this.sendLock)
        {
            startDeadline = !this.IsClosing && !this.disconnectRequested;
            if (startDeadline)
            {
                this.disconnectRequested = true;
                this.WriteOutgoing(new Outgoing(
                    Packet: null, RunsHandlers: false, Work: null, DisconnectReason: reason));
                this.packetQueue.Writer.TryComplete();
            }
        }

        // Neither engine callers nor a handler kicking its own client wait for the outgoing pipeline.
        if (startDeadline)
            this.ObserveOutgoingTask(this.CloseAfterTimeoutAsync());

        return new ValueTask(this.LeaveAsync());
    }

    private async Task CloseAfterTimeoutAsync()
    {
        try
        {
            await Task.Delay(DisconnectTimeout, this.cancellationToken);
            Log.DisconnectStalled(this.Logger, DisconnectTimeout);
            this.Disconnect();
        }
        catch (OperationCanceledException) when (this.cancellationToken.IsCancellationRequested)
        {
            // The terminal entry or the peer already closed the connection.
        }
    }

    /// <summary>
    /// Queues <paramref name="packet"/> behind the packets queued already. Plugins' <see cref="QueuePacketEventArgs"/>
    /// handlers see it as its turn comes, in the client's pipeline, and it's sent right after them unless one cancelled
    /// it or the client can no longer receive it.
    /// </summary>
    /// <returns>A completed task: queueing never waits for the handlers, which may wait for the caller.</returns>
    public ValueTask QueuePacketAsync(IClientboundPacket packet)
    {
        this.Enqueue(new Outgoing(packet, RunsHandlers: true, Work: null));

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Sends <paramref name="packet"/> without plugins' handlers: at once when nothing queued before it is still on its
    /// way, or else behind what is, so that it never overtakes a packet queued earlier. Sending it at once serializes it
    /// on the caller's thread; the network threads compress, encrypt and write it (see <see cref="WorkerThreads.Network"/>).
    /// </summary>
    /// <returns>Whether it's sent or on its way: the client can receive it.</returns>
    public bool SendPacket(IClientboundPacket packet) => this.SendPacket(packet, shared: null);

    /// <summary>
    /// Sends a broadcast's packet like <see cref="SendPacket(IClientboundPacket)"/>, with the bodies serialized for the
    /// connections that write it like this one. Call it on the broadcasting thread, which owns
    /// <paramref name="shared"/>: a send that waits its turn takes the bodies along rather than serializing it later.
    /// </summary>
    internal bool SendPacket(SharedPacketBodies shared) => this.SendPacket(shared.Packet, shared);

    private bool SendPacket(IClientboundPacket packet, SharedPacketBodies? shared)
    {
        lock (this.sendLock)
        {
            if (this.IsClosing || this.disconnectRequested)
                return false;

            // Admission and the direct write share the lock, so a new entry cannot slip between them.
            if (Volatile.Read(ref this.unsent) == 0)
                return this.SendAsync(packet, bodies: shared?.Get(this.ReadsPluginEntityData(packet)));
        }

        var bodies = shared?.Get(this.ReadsPluginEntityData(packet));
        return this.Enqueue(new Outgoing(packet, RunsHandlers: false, Work: null, Bodies: bodies));
    }

    /// <summary>
    /// Runs <paramref name="work"/> in the client's pipeline, in turn with its packets: after those queued before it
    /// were sent, and before those queued after it. The work can run plugins' handlers for a packet
    /// (<see cref="RunQueueHandlersAsync"/>), check what it sends after them and send it (<see cref="SendInTurn"/>).
    /// </summary>
    /// <remarks>
    /// It runs where plugins' handlers run, so it must never wait for anything the pipeline sends. It's dropped, like
    /// packets, once the client disconnected. A failure is logged.
    /// </remarks>
    internal void QueueInTurn(Func<ValueTask> work) =>
        this.Enqueue(new Outgoing(Packet: null, RunsHandlers: false, work));

    /// <summary>
    /// Runs plugins' handlers for queueing <paramref name="packet"/>, without queueing it; for work run in turn (see
    /// <see cref="QueueInTurn"/>), where the pipeline runs them for queued packets.
    /// </summary>
    /// <returns>Whether it's still to be sent: the client can receive it and no handler cancelled it.</returns>
    internal async ValueTask<bool> RunQueueHandlersAsync(IClientboundPacket packet)
    {
        if (!this.CanReceive(packet))
            return false;

        try
        {
            var args = new QueuePacketEventArgs(this.HostServer, this, packet);
            var result = await this.eventDispatcher.ExecuteEventAsync(args);
            if (result == EventResult.Cancelled)
            {
                Logger.LogDebug(
                    "Packet {PacketId} was sent to the queue, however an event handler has cancelled it.",
                    args.Packet.Id);
                return false;
            }

            return true;
        }
        catch (OperationCanceledException) when (this.cancellationToken.IsCancellationRequested)
        {
            // The client disconnected while handlers ran: the packet is dropped.
            return false;
        }
    }

    /// <summary>
    /// Sends <paramref name="packet"/> now, without plugins' handlers; only from work run in turn (see
    /// <see cref="QueueInTurn"/>), whose turn it is.
    /// </summary>
    internal bool SendInTurn(IClientboundPacket packet) => this.SendAsync(packet);

    // Adds an entry to the pipeline, unless the client can't receive it. A play packet queued while the client goes
    // back to configuration is dropped here, as later at its turn and when it's written (see Accepts).
    private bool Enqueue(Outgoing entry)
    {
        lock (this.sendLock)
        {
            if (this.disconnectRequested
                || (entry.Packet is IClientboundPacket packet ? !this.CanReceive(packet) : this.IsClosing))
                return false;

            if (Volatile.Read(ref this.unsent) < MaximumOutgoingEntries)
                return this.WriteOutgoing(entry);

            // Like vanilla's overflowing connections, shed the client rather than waiting for its pipeline.
            Log.OutgoingOverflow(this.Logger, MaximumOutgoingEntries);
            this.disconnectRequested = true;
        }

        this.Disconnect();
        return false;
    }

    // Called under sendLock, including for the one terminal entry allowed beyond the admission limit.
    private bool WriteOutgoing(Outgoing entry)
    {
        Interlocked.Increment(ref this.unsent);
        if (this.packetQueue.Writer.TryWrite(entry))
            return true;

        Interlocked.Decrement(ref this.unsent);
        return false;
    }

    private bool IsClosing => this.disposed || this.cancellationToken.IsCancellationRequested;

    private bool CanReceive(IClientboundPacket packet) => !this.IsClosing && this.Connected && this.Accepts(packet);

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
        var threshold = this.HostServer.Configuration.Network.CompressionThreshold;
        if (threshold < 0 || this.CompressionEnabled)
            return;

        // Its announcement uses the old framing; the threshold changes under the same lock as every write.
        this.QueueInTurn(() =>
        {
            lock (this.sendLock)
            {
                if (!this.CompressionEnabled && this.SendAsync(new LoginCompressionPacket(threshold)))
                    this.compressionThreshold = threshold;
            }

            return ValueTask.CompletedTask;
        });
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
        }

        lock (this.sendLock)
        {
            this.packetQueue.Writer.TryComplete();
            while (this.packetQueue.Reader.TryRead(out var entry))
            {
                entry.Flushed?.TrySetResult(0);
                Interlocked.Decrement(ref this.unsent);
            }

            this.outputDrained?.TrySetResult();
        }

        this.cancellationSource.Cancel();
        this.cancellationSource.Dispose();
        try
        {
            this.Socket?.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }

        this.Socket?.Dispose();
        this.ObserveOutgoingTask(this.packetQueueTask);

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
        }

        this.Dispose();
        Disconnected?.Invoke(this);

        // A player whose client closed the connection (as vanilla's clients do to quit) leaves too and is saved, and
        // other players stop seeing them. A player still logging in or configuring hasn't joined, so there's nothing to
        // leave.
        if (this.State == ClientState.Play)
            _ = this.LeaveAsync();

        this.HostServer.Connections.Remove(this.Id, out _);

        if (this.Player is not null)
            this.HostServer.RemovePlayer(this.Player);

        this.receiving = false;

        // Under the lock sends are submitted under, so none sets up the send event once it's returned (see TrySend).
        lock (this.sendLock)
        {
            if (this.receiveEvent is not null)
            {
                this.receiveEvent.Completed -= this.OnAsyncCompleted;
                this.pool.Return(this.receiveEvent);
            }

            if (this.sendEvent is not null)
            {
                this.sendEvent.Completed -= this.OnAsyncCompleted;
                this.pool.Return(this.sendEvent);
            }

            this.sending = false;
            this.pendingFrames.Clear();
            this.sendBufferMain.Clear();
            this.sendBufferFlush.Clear();

            this.sendBufferFlushOffset = 0;
        }
    }

    private async Task<MojangProfile?> HasJoinedAsync() => await this.userCache.HasJoinedAsync(this.Player!.Username, this.ServerId!);

    // The pipeline's reader: takes each entry in turn, runs plugins' handlers for it and sends it. A handler that takes
    // long holds up only this client's later packets; one that fails is logged and the pipeline goes on.
    private async Task HandlePacketQueueAsync()
    {
        var cancellationToken = this.cancellationToken;

        try
        {
            while (await this.packetQueue.Reader.WaitToReadAsync(cancellationToken))
            {
                if (!this.packetQueue.Reader.TryRead(out var entry))
                    continue;

                try
                {
                    var pending = this.SendQueuedAsync(entry);
                    if (pending.IsCompleted)
                    {
                        await pending;
                    }
                    else
                    {
                        var task = pending.AsTask();
                        try
                        {
                            await task.WaitAsync(cancellationToken);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            // A plugin may never return; release the consumer and observe a possible late failure.
                            this.ObserveOutgoingTask(task);
                            throw;
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    Log.QueuedPacketFailed(this.Logger, exception, entry.Packet?.Id);
                }
                finally
                {
                    entry.Flushed?.TrySetResult(0);
                    Interlocked.Decrement(ref this.unsent);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client disconnected: what's left in the pipeline is dropped.
        }
    }

    private async ValueTask SendQueuedAsync(Outgoing entry)
    {
        if (this.IsClosing)
            return;

        if (entry.DisconnectReason is ChatMessage reason)
        {
            try
            {
                Task drained;
                lock (this.sendLock)
                {
                    IClientboundPacket terminal = this.State == ClientState.Login
                        ? new LoginDisconnectPacket { ReasonJson = reason.ToString(Globals.JsonOptions) }
                        : this.State == ClientState.Play ? DisconnectPacket.ClientboundPlay with { Reason = reason }
                        : DisconnectPacket.ClientboundConfiguration with { Reason = reason };

                    // Terminal packets cannot be cancelled by plugins or rejected during reconfiguration.
                    this.SendAsync(terminal, terminal: true);
                    drained = this.sending || this.flushScheduled
                        ? (this.outputDrained ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task
                        : Task.CompletedTask;
                }

                await drained.WaitAsync(this.cancellationToken);
            }
            finally
            {
                this.Disconnect();
            }

            return;
        }

        if (entry.Flushed is TaskCompletionSource<int> flushed)
        {
            flushed.TrySetResult(Volatile.Read(ref this.unsent) - 1);
            return;
        }

        if (entry.Work is not null)
        {
            await entry.Work();
            return;
        }

        var packet = entry.Packet!;
        if (!entry.RunsHandlers || await this.RunQueueHandlersAsync(packet))
            this.SendAsync(packet, bodies: entry.Bodies);
    }

    /// <summary>
    /// Completes once everything queued so far was sent or dropped, along with what plugins' handlers queued meanwhile.
    /// For tests: called from a handler, it would wait for itself.
    /// </summary>
    internal async Task FlushAsync()
    {
        while (!this.IsClosing)
        {
            // How many entries were queued behind the marker by the time it ran.
            var flushed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!this.Enqueue(new Outgoing(Packet: null, RunsHandlers: false, Work: null, Flushed: flushed)))
                return;

            if (await flushed.Task == 0)
                return;
        }
    }

    // Disposal is synchronous and plugins may hang, so observe tasks without waiting on their code.
    private void ObserveOutgoingTask(Task task) => _ = task.ContinueWith(
        failed => Log.QueuedPacketFailed(this.Logger, failed.Exception!, null),
        CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);

    // An entry of the pipeline: a packet, which plugins' handlers see first unless it's sent as is, or work run in turn
    // with the packets (see QueueInTurn). A broadcast's packet sent as is carries the bodies serialized for it.
    private readonly record struct Outgoing(
        IClientboundPacket? Packet,
        bool RunsHandlers,
        Func<ValueTask>? Work,
        TaskCompletionSource<int>? Flushed = null,
        ChatMessage? DisconnectReason = null,
        IReadOnlyList<NetworkBuffer>? Bodies = null);


    private async ValueTask<bool> HandlePacketAsync(PacketData packetData)
    {
        // A closing connection's packets still buffered aren't handled: the player has left, and a packet that joins
        // them (finishing configuration, say) would bring them back.
        lock (this.sendLock)
        {
            if (this.IsClosing || this.disconnectRequested)
                return false;
        }

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
            if (this.disconnected || this.IsClosing || this.disconnectRequested)
                return;

            this.HostServer.Connections.Remove(this.Id, out _);

            this.Id = Obsidian.Server.GetNextEntityId();

            this.HostServer.Connections.TryAdd(this.Id, this);

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
        if (waited > this.HostServer.Configuration.Network.KeepAliveTimeoutInterval)
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

        // The connection's one leave, until the player joins again. It's started, and tracked, before the player stops
        // being online, so that the player joining again elsewhere waits for it.
        var leave = this.LeaveAsync();

        this.HostServer.RemovePlayer(player);
        await player.StopSendingChunksAsync();
        await leave;
        await this.QueuePacketAsync(new Net.Packets.Play.Clientbound.StartConfigurationPacket());
    }

    /// <summary>
    /// Starts the configuration handshake, recording the server's content first: what the handshake agrees on is at
    /// least as new, so a later change is told apart.
    /// </summary>
    internal void StartConfiguration()
    {
        this.ConfiguredContent = ((Server)this.HostServer).GetContentVersion();
        this.ConfiguredPluginMenus = MenuTypesRegistry.PluginMenus.Count;
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
        // earlier leave finished before the switch. The player is replaced under the leave's lock, so that a leave
        // starting meanwhile is the old player's or the new one's, never the old one's after the switch.
        var fresh = this.CreatePlayer(player.Uuid, player.Username, this.HostServer.DefaultWorld);
        fresh.SkinProperties = player.SkinProperties;

        lock (this.leavingLock)
        {
            // A connection closing meanwhile doesn't join again.
            if (this.cancellationSource.IsCancellationRequested)
                return;

            this.Player = fresh;
            this.leaving = null;
        }

        var concreteServer = (Server)this.HostServer;
        this.Channels = new ClientChannels(this, concreteServer.PayloadChannels, concreteServer.GetModdedRegistries);
        ((ConfigurationClientHandler)this.handlers[ClientState.Configuration]).Reset();

        this.StartConfiguration();
    }

    private Player CreatePlayer(Guid uuid, string username, IWorld world) => new(uuid, username, this, world)
    {
        Server = this.serviceProvider.GetRequiredService<IServer>(),

        // Vanilla's isSingleplayerOwner: an integrated server's local player.
        IsSingleplayerOwner = this.HostServer is Server { Integrated: IntegratedSession integrated }
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
        if (raisingLeave.Value == this)
            return Task.CompletedTask;

        Player player;
        bool inOwnTick;
        TaskCompletionSource leave;
        lock (this.leavingLock)
        {
            if (this.Player is not Player current)
                return Task.CompletedTask;

            player = current;

            // The leave waits for a tick of the player under way, so a leave from inside one isn't waited for.
            inOwnTick = player.IsInOwnTick;

            if (this.leaving is TaskCompletionSource started)
                return inOwnTick ? Task.CompletedTask : started.Task;

            leave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            this.leaving = leave;

            // Tracked before it starts, so a shutdown, or the player joining again, that begins meanwhile waits for it.
            if (this.HostServer is Server server)
                server.TrackLeave(leave.Task, player.Username);
        }

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
                // The player's ticks stop first, so the leave saves them as the last tick left them. A tick under way
                // may have moved them to another level (through a portal) after they were removed, so they're removed
                // from where they are now too.
                await player.StopTickingAsync();
                player.Level.TryRemovePlayer(player);

                var leaving = new PlayerLeaveEventArgs(player, this.HostServer, DateTimeOffset.Now);
                result = await this.eventDispatcher.ExecuteEventAsync(leaving);
            }
            catch (Exception ex)
            {
                Log.LeaveFailed(this.Logger, ex, player.Username);
                result = EventResult.Failed;
            }

            // Leaving saves the player, so a failed leave may have lost their data; it's reported like a failed save.
            if (result == EventResult.Failed && this.HostServer is Server server)
                server.ReportPlayerSaveFailed(player.Username);
        }
        finally
        {
            // No longer a viewer of what they had open, so other viewers' clicks don't update them.
            player.OpenedContainer = null;
            player.RunMenusClosed();
            leave.SetResult();
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Disconnect output did not drain within {Timeout}; closing client")]
        public static partial void DisconnectStalled(ILogger logger, TimeSpan timeout);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Outgoing packet backlog exceeded {Limit}; disconnecting client")]
        public static partial void OutgoingOverflow(ILogger logger, int limit);

        [LoggerMessage(Level = LogLevel.Error, Message = "Handling {Username} leaving failed")]
        public static partial void LeaveFailed(ILogger logger, Exception exception, string username);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to authenticate {Username}")]
        public static partial void AuthenticationFailed(ILogger logger, string? username);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Handling packet {PacketId} in state {State} failed")]
        public static partial void PacketHandlingFailed(ILogger logger, Exception exception, int packetId, ClientState state);

        [LoggerMessage(Level = LogLevel.Error, Message = "Sending queued packet {PacketId} failed")]
        public static partial void QueuedPacketFailed(ILogger logger, Exception exception, int? packetId);

        [LoggerMessage(Level = LogLevel.Error, Message = "Writing the client's output failed; disconnecting it")]
        public static partial void OutputFailed(ILogger logger, Exception exception);

        [LoggerMessage(Level = LogLevel.Error, Message = "Disconnecting the client failed")]
        public static partial void DisconnectFailed(ILogger logger, Exception exception);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Disconnecting client after socket error {SocketError}")]
        public static partial void SocketFailed(ILogger logger, SocketError socketError);
    }
}
