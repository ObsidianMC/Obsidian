using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;
using Obsidian.API.Events;
using Obsidian.Blocks;
using Obsidian.Entities;
using Obsidian.Events.EventArgs;
using Obsidian.Net;
using Obsidian.Net.ClientHandlers;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Common;
using Obsidian.Net.Packets.Handshake.Serverbound;
using Obsidian.Net.Packets.Login.Clientbound;
using Obsidian.Net.Packets.Status.Clientbound;
using Obsidian.Services;
using Obsidian.Utilities.Mojang;
using Obsidian.WorldData;
using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;

namespace Obsidian;

public sealed class Client : IDisposable
{
    /// <summary>
    /// The player's entity id.
    /// </summary>
    internal int id;

    /// <summary>
    /// How many <see cref="KeepAlivePacket"/>s the client has missed.
    /// </summary>
    internal long? lastKeepAliveId;

    /// <summary>
    /// The public key/signature data received from mojang.
    /// </summary>
    internal SignatureData? signatureData;

    /// <summary>
    /// Used for signing chat messages.
    /// </summary>
    internal SignedMessage? messageSigningData;

    /// <summary>
    /// The server that the client is connected to.
    /// </summary>
    internal readonly Server server;

    private readonly IUserCache userCache;

    /// <summary>
    /// Whether the client has compression enabled on the Minecraft stream.
    /// </summary>
    private bool compressionEnabled;

    /// <summary>
    /// Whether this client is disposed.
    /// </summary>
    private bool disposed;

    /// <summary>
    /// The random token used to encrypt the stream.
    /// </summary>
    internal byte[]? randomToken;

    /// <summary>
    /// The server's token used to encrypt the stream.
    /// </summary>
    private byte[]? sharedKey;

    /// <summary>
    /// The stream used to receive and send packets.
    /// </summary>
    private MinecraftStream minecraftStream;

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
    /// The base network stream used by the <see cref="minecraftStream"/>.
    /// </summary>
    private readonly DuplexPipeStream networkStream;

    /// <summary>
    /// Used to continuously send and receive encrypted packets from the client.
    /// </summary>
    private readonly PacketCryptography packetCryptography;

    /// <summary>
    /// The connection context associated with the <see cref="networkStream"/>.
    /// </summary>
    private readonly ConnectionContext connectionContext;
    private readonly ILoggerFactory loggerFactory;

    private string? ServerId => sharedKey?.Concat(packetCryptography.PublicKey).MinecraftShaDigest();

    /// <summary>
    /// The client's ping in milliseconds.
    /// </summary>
    public int Ping { get; internal set; }

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
    public IPEndPoint? RemoteEndPoint => connectionContext.RemoteEndPoint as IPEndPoint;

    public string? Ip => this.RemoteEndPoint?.Address.ToString();

    /// <summary>
    /// Executed when the client disconnects.
    /// </summary>
    public event Action<Client>? Disconnected;

    /// <summary>
    /// Used to log actions caused by the client.
    /// </summary>
    public ILogger Logger { get; private set; }

    /// <summary>
    /// The player that the client is logged in as.
    /// </summary>
    public Player? Player { get; private set; }

    /// <summary>
    /// The client brand. This is the name that the client used to identify itself (Fabric, Forge, Quilt, etc.)
    /// </summary>
    public string? Brand { get; internal set; }

    public Client(ConnectionContext connectionContext,
        ILoggerFactory loggerFactory, IUserCache playerCache,
        Server server)
    {
        this.connectionContext = connectionContext;
        this.loggerFactory = loggerFactory;
        this.server = server;
        this.userCache = playerCache;
        this.Logger = loggerFactory.CreateLogger("ConnectionHandler");

        packetCryptography = new();
        this.handlers = new Dictionary<ClientState, ClientHandler>()
        {
            { ClientState.Login, new LoginClientHandler { Client = this } },
            { ClientState.Configuration, new ConfigurationClientHandler { Client = this } },
            { ClientState.Play, new PlayClientHandler { Client = this } }
        }.ToFrozenDictionary();

        networkStream = new(connectionContext.Transport);
        minecraftStream = new(networkStream);

        packetQueue = Channel.CreateUnbounded<IClientboundPacket>(new() { SingleReader = true, SingleWriter = true });
    }

    private async ValueTask<PacketData> GetNextPacketAsync()
    {
        var length = await minecraftStream.ReadVarIntAsync();
        var receivedData = ArrayPool<byte>.Shared.Rent(length);

        _ = await minecraftStream.ReadAsync(receivedData.AsMemory(0, length));

        byte[] packetData = default!;
        int packetId = default!;

        var error = false;
        using (var packetStream = new MinecraftStream(receivedData))
        {
            try
            {
                packetId = await packetStream.ReadVarIntAsync();
                var arlen = 0;

                if (length - packetId.GetVarIntLength() > -1)
                    arlen = length - packetId.GetVarIntLength();

                packetData = ArrayPool<byte>.Shared.Rent(arlen);
                _ = await packetStream.ReadAsync(packetData.AsMemory(0, packetData.Length));
            }
            catch (Exception ex)
            {
                this.Logger.LogCritical(ex, "Failed to get next packet.");

                error = true;
            }
        }

        ArrayPool<byte>.Shared.Return(receivedData);

        return error ? PacketData.Default : new PacketData { Id = packetId, Data = packetData, IsDisposable = true };
    }

    private async Task HandlePacketQueueAsync()
    {
        try
        {
            while (!cancellationSource.IsCancellationRequested && this.connectionContext.IsConnected())
            {
                var packet = await this.packetQueue.Reader.ReadAsync(this.cancellationSource.Token);

                this.SendPacket(packet);
            }
        }
        catch (OperationCanceledException)
        {
            this.Logger.LogDebug("Client({id}) packet queue was cancelled", this.id);
        }
    }

    private async Task HandlePacketsAsync()
    {
        try
        {
            while (!cancellationSource.IsCancellationRequested && this.connectionContext.IsConnected())
            {
                using var packetData = await GetNextPacketAsync();

                if (State == ClientState.Play && packetData.Data.Length < 0)//Empty packets get sent.
                    Disconnect();

                switch (State)
                {
                    case ClientState.Status: // Server ping/list
                        if (packetData.Id == 0x00)
                        {
                            var status = new ServerStatus(this.server, this.loggerFactory);

                            _ = await this.server.EventDispatcher.ExecuteEventAsync(new ServerStatusRequestEventArgs(this.server, status));

                            SendPacket(new StatusResponsePacket(status));
                        }
                        else if (packetData.Id == 0x01)
                        {
                            var pong = Net.Packets.Status.Serverbound.PingRequestPacket.Deserialize(packetData.Data);

                            SendPacket(new PongResponsePacket { Timestamp = pong.Timestamp });
                            Disconnect();
                        }
                        break;

                    case ClientState.Handshaking:
                        if (packetData.Id == 0x00)
                        {
                            await IntentionPacket.Deserialize(packetData.Data).HandleAsync(this);
                        }
                        else
                        {
                            // Handle legacy ping
                        }
                        break;

                    case ClientState.Login:
                        await this.HandlePacketAsync(packetData);
                        break;
                    case ClientState.Configuration:
                        Debug.Assert(Player is not null);

                        var result = await this.server.EventDispatcher.ExecuteEventAsync(new PacketReceivedEventArgs(Player, this.server, packetData.Id, packetData.Data));

                        if (result == EventResult.Cancelled)
                        {
                            this.Logger.LogDebug("configuration packet({id}) {name} was cancelled and is not being processed.",
                                packetData.Id, PacketsRegistry.Configuration.ServerboundNames[packetData.Id]);
                            return;
                        }

                        await this.HandlePacketAsync(packetData);
                        break;
                    case ClientState.Play:
                        Debug.Assert(Player is not null);

                        result = await this.server.EventDispatcher.ExecuteEventAsync(new PacketReceivedEventArgs(Player, this.server, packetData.Id, packetData.Data));

                        if (result == EventResult.Cancelled)
                        {
                            this.Logger.LogDebug("play packet({id}) {name} was cancelled and is not being processed.",
                                packetData.Id, PacketsRegistry.Play.ServerboundNames[packetData.Id]);
                            return;
                        }

                        await this.HandlePacketAsync(packetData);

                        break;
                    case ClientState.Closed:
                    default:
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            this.Logger.LogDebug("Client({id}) main loop was cancelled", this.id);
        }
    }

    public async Task StartConnectionAsync()
    {
        await Task.WhenAll([this.HandlePacketsAsync(), this.HandlePacketQueueAsync()]);

        Logger.LogInformation("Disconnected client");

        if (State == ClientState.Play)
        {
            Debug.Assert(Player is not null);
            await this.server.EventDispatcher.ExecuteEventAsync(new PlayerLeaveEventArgs(Player, this.server, DateTimeOffset.Now));
        }

        Disconnected?.Invoke(this);
        this.Dispose();//Dispose client after
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
        else if (this.server.Configuration.Whitelist && !this.server.IsWhitedlisted(this.profile.Uuid))
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
    public void Initialize(World world)
    {
        if (this.profile == null)
            throw new UnreachableException("Profile was not set or is null.");

        this.Player = new(this.profile.Uuid, this.profile.Name, this, world);

        this.packetCryptography.GenerateKeyPair();

        var (publicKey, randomToken) = this.packetCryptography.GeneratePublicKeyAndToken();

        this.randomToken = randomToken;

        this.SendPacket(new HelloPacket
        {
            PublicKey = publicKey,
            VerifyToken = randomToken,
            ShouldAuthenticate = true//I don't know how we're supposed to use this
        });
    }

    public void InitializeOffline(string username, World world)
    {
        this.InitializeId();

        this.Player = new Player(GuidHelper.FromStringHash($"OfflinePlayer:{username}"), username, this, world);

        this.SendPacket(new LoginFinishedPacket(Player.Uuid, Player.Username)
        {
            SkinProperties = this.Player.SkinProperties,
        });

        this.Logger.LogDebug("Sent Login success to user {Username} {UUID}", this.Player.Username, this.Player.Uuid);
    }

    private void InitializeId()
    {
        this.id = Server.GetNextEntityId();
        this.Logger = this.loggerFactory.CreateLogger($"Client({this.id})");
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

    public async ValueTask DisconnectAsync(ChatMessage reason)
    {
        if (this.State == ClientState.Login)
        {
            await this.QueuePacketAsync(new LoginDisconnectPacket { ReasonJson = reason.ToString(Globals.JsonOptions) });
            return;
        }

        await this.QueuePacketAsync(new DisconnectPacket { Reason = reason });
    }

    public async ValueTask QueuePacketAsync(IClientboundPacket packet)
    {
        if (this.cancellationSource.IsCancellationRequested)
            return;

        var args = new QueuePacketEventArgs(this.server, this, packet);

        var result = await this.server.EventDispatcher.ExecuteEventAsync(args);
        if (result == EventResult.Cancelled)
        {
            Logger.LogDebug("Packet {PacketId} was sent to the queue, however an event handler has cancelled it.", args.Packet.Id);
        }
        else
        {
            await packetQueue.Writer.WriteAsync(packet, this.cancellationSource.Token);
        }
    }

    internal void SendPacket(IClientboundPacket packet)
    {
        try
        {
            if (!compressionEnabled)
            {
                this.minecraftStream.WritePacket(packet);
            }
            else
            {
                this.minecraftStream.WriteCompressedPacket(packet, server.Configuration.Network.CompressionThreshold);
            }
        }
        catch (SocketException)
        {
            // Clients can disconnect at any point, causing exception to be raised
            if (!connectionContext.IsConnected())
            {
                Disconnect();
            }
        }
        catch (Exception e)
        {
            var packetId = packet.Id;
            var packetName = packet.Id.ToString();

            if (this.State == ClientState.Login)
                packetName = PacketsRegistry.Login.ClientboundNames[packetId];
            else if (this.State == ClientState.Configuration)
                packetName = PacketsRegistry.Configuration.ClientboundNames[packetId];
            else if (this.State == ClientState.Play)
                packetName = PacketsRegistry.Play.ClientboundNames[packetId];
            else if (this.State == ClientState.Status)
                packetName = PacketsRegistry.Status.ClientboundNames[packetId];

            Logger.LogDebug(e, "Sending {state} packet({id}) {name} failed", this.State, packetId, packetName);
        }
    }

    internal void Disconnect()
    {
        cancellationSource.Cancel();
        Disconnected?.Invoke(this);

        this.Dispose();
    }

    internal void Login(MojangProfile user)
    {
        this.Player!.SkinProperties = user.Properties!;
        this.EncryptionEnabled = true;
        this.minecraftStream = new EncryptedMinecraftStream(networkStream, sharedKey!);

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

        if (this.randomToken is null)
            throw new InvalidOperationException("Received Encryption Response before sending Encryption Request.");
    }

    internal void SetState(ClientState state) => this.State = state;

    public async Task<MojangProfile?> HasJoinedAsync() => await this.userCache.HasJoinedAsync(this.Player!.Username, this.ServerId!);

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        minecraftStream.Dispose();
        connectionContext.Abort();
        cancellationSource?.Dispose();

        GC.SuppressFinalize(this);
    }
}
