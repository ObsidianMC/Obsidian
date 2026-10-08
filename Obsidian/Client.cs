using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Events.EventArgs;
using Obsidian.Integrated;
using Obsidian.Net;
using Obsidian.Net.ClientHandlers;
using Obsidian.Net.Packets.Common;
using Obsidian.Net.Packets.Login.Clientbound;
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

    // Set once the player's leave was raised; see LeaveAsync.
    private int left;

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
    public bool CompressionEnabled { get; private set; }

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

        packetQueue = Channel.CreateUnbounded<IClientboundPacket>(new() { SingleReader = true, SingleWriter = true });
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
        if (!this.Connected)
            return;

        var args = new QueuePacketEventArgs(this.Server, this, packet);

        var result = await this.eventDispatcher.ExecuteEventAsync(args);
        if (result == EventResult.Cancelled)
            return;

        try
        {
            await packetQueue.Writer.WriteAsync(packet, this.cancellationSource.Token);
        }
        catch (OperationCanceledException) when (this.cancellationSource.IsCancellationRequested)
        {
            // The connection closed meanwhile, e.g. the client quit as the server stopped; there's nobody to send to.
        }
    }

    public bool SendPacket(IClientboundPacket packet) => this.SendAsync(packet);

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
        if (disposed)
            return;

        disposed = true;

        try
        {
            cancellationSource?.Dispose();

            this.Socket.Dispose();
        }
        catch (ObjectDisposedException) { }

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
        cancellationSource.Cancel();
        Disconnected?.Invoke(this);

        // The player also leaves (and is saved) when their client closed the connection, as vanilla's clients do to quit.
        var leaving = this.LeaveAsync();
        if (this.Server is Server server)
            server.TrackLeave(leaving);

        this.receiveEvent.Completed -= this.OnAsyncCompleted;
        this.sendEvent.Completed -= this.OnAsyncCompleted;

        this.pool.Return(this.receiveEvent);
        this.pool.Return(this.sendEvent);

        var removed = this.Server.Connections.Remove(this.Id, out _);

        if (this.Player != null)
            this.Server.RemovePlayer(this.Player);

        Log.Disconnected(this.Logger, this.Ip);

        try
        {
            this.Socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException) { }

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
            while (this.Connected || !this.disposed || !this.cancellationSource.IsCancellationRequested)
            {
                var packet = await this.packetQueue.Reader.ReadAsync(this.cancellationSource.Token);

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
        this.Server.Connections.Remove(this.Id, out _);

        this.Id = Obsidian.Server.GetNextEntityId();

        this.Server.Connections.TryAdd(this.Id, this);

        this.Logger = this.loggerFactory.CreateLogger($"Client({this.Id})");
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
    /// (<see cref="DisconnectAsync"/>) or by the client (<see cref="Disconnect"/>).
    /// </summary>
    private async Task LeaveAsync()
    {
        if (this.Player is null || Interlocked.Exchange(ref this.left, 1) == 1)
            return;

        EventResult result;
        try
        {
            result = await this.eventDispatcher.ExecuteEventAsync(new PlayerLeaveEventArgs(this.Player, this.Server, DateTimeOffset.Now));
        }
        catch (Exception ex)
        {
            Log.LeaveFailed(this.Logger, ex, this.Player.Username);
            result = EventResult.Failed;
        }

        // Leaving saves the player, so a failed leave may have lost their data; it's reported like a failed save.
        if (result == EventResult.Failed && this.Server is Server server)
            server.ReportPlayerSaveFailed(this.Player.Username);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Handling {Username} leaving failed")]
        public static partial void LeaveFailed(ILogger logger, Exception exception, string username);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to authenticate {Username}")]
        public static partial void AuthenticationFailed(ILogger logger, string? username);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Client {Ip} disconnected")]
        public static partial void Disconnected(ILogger logger, string? ip);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Handling packet {PacketId} in state {State} failed")]
        public static partial void PacketHandlingFailed(ILogger logger, Exception exception, int packetId, ClientState state);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Disconnecting client after socket error {SocketError}")]
        public static partial void SocketFailed(ILogger logger, SocketError socketError);
    }
}
