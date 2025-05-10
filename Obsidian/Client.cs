using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Events.EventArgs;
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
    /// The random token used to encrypt the stream.
    /// </summary>
    public byte[]? RandomToken { get; private set; }

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

    public bool Connected { get; private set; }

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

    public ReadOnlySpan<byte> SetSharedKeyAndDecodeVerifyToken(byte[] secret, byte[] verifyToken)
    {
        this.sharedKey = packetCryptography.Decrypt(secret);
        return this.packetCryptography.Decrypt(verifyToken);
    }

    public void Initialize(IWorld world)
    {
        if (this.profile == null)
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

    public void InitializeOffline(string username, IWorld world)
    {
        this.InitializeId();

        this.Player = this.CreatePlayer(GuidHelper.FromStringHash($"OfflinePlayer:{username}"), username, world);

        this.SendPacket(new LoginFinishedPacket(Player.Uuid, Player.Username)
        {
            SkinProperties = this.Player.SkinProperties,
        });

        this.Logger.LogDebug("Sent Login success to user {Username} {UUID}", this.Player.Username, this.Player.Uuid);
    }

    public async ValueTask DisconnectAsync(ChatMessage reason)
    {
        if (this.Player != null)
            await this.eventDispatcher.ExecuteEventAsync(new PlayerLeaveEventArgs(this.Player, this.Server, DateTimeOffset.Now));

        if (this.State == ClientState.Login)
        {
            await this.QueuePacketAsync(new LoginDisconnectPacket { ReasonJson = reason.ToString(Globals.JsonOptions) });
            return;
        }

        await this.QueuePacketAsync(new DisconnectPacket { Reason = reason });
    }

    public async ValueTask QueuePacketAsync(IClientboundPacket packet)
    {
        if (!this.Connected)
            return;

        var args = new QueuePacketEventArgs(this.Server, this, packet);

        var result = await this.eventDispatcher.ExecuteEventAsync(args);
        if (result == EventResult.Cancelled)
        {
            Logger.LogDebug("Packet {PacketId} was sent to the queue, however an event handler has cancelled it.", args.Packet.Id);

            return;
        }

        await packetQueue.Writer.WriteAsync(packet, this.cancellationSource.Token);
    }

    public bool SendPacket(IClientboundPacket packet) => this.SendAsync(packet);

    //TODO ENCRYPTION
    internal void Login(MojangProfile user)
    {
        this.Player!.SkinProperties = user.Properties!;
        this.EncryptionEnabled = true;
        //this.minecraftStream = new EncryptedMinecraftStream(networkStream, sharedKey!);

        this.SendPacket(new LoginFinishedPacket(Player.Uuid, Player.Username)
        {
            SkinProperties = this.Player.SkinProperties,
        });

        this.Logger.LogDebug("Sent Login success to user {Username} {UUID}", this.Player.Username, this.Player.Uuid);
    }

    internal void ThrowIfInvalidEncryptionRequest()
    {
        if (this.Player is null)
            throw new InvalidOperationException("Received Encryption Response before sending Login Start.");

        if (this.RandomToken is null)
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
            this.Logger.LogWarning("Failed to auth {Username}", this.Player?.Username);
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

        this.receiveEvent.Completed -= this.OnAsyncCompleted;
        this.sendEvent.Completed -= this.OnAsyncCompleted;

        this.pool.Return(this.receiveEvent);
        this.pool.Return(this.sendEvent);

        var removed = this.Server.Connections.Remove(this.Id, out _);

        if (this.Player != null)
            this.Server.OnlinePlayers.Remove(this.Player.Uuid, out _);

        this.Logger.LogInformation("Client {ip} disconnected.", this.Ip);

        try
        {
            this.Socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException) { }

        this.Socket.Close();

        this.Connected = false;

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

                string name = "";

                if (this.State == ClientState.Login)
                    PacketsRegistry.Login.ClientboundNames.TryGetValue(packet.Id, out name);
                else if (this.State == ClientState.Configuration)
                    PacketsRegistry.Configuration.ClientboundNames.TryGetValue(packet.Id, out name);
                else if (this.State == ClientState.Play)
                    PacketsRegistry.Play.ClientboundNames.TryGetValue(packet.Id, out name);

                this.Logger.LogTrace("Sending packet({name})", name);

                this.SendPacket(packet);
            }
        }
        catch (OperationCanceledException)
        {
            this.Logger.LogDebug("Client({id}) packet queue was cancelled", this.Id);
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
            this.Logger.LogDebug(ex, "An error has occured handling packet");
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
        Server = this.serviceProvider.GetRequiredService<IServer>()
    };
}
