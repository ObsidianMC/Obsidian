using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;
using Obsidian.API.Events;
using Obsidian.Concurrency;
using Obsidian.Entities;
using Obsidian.Events.EventArgs;
using Obsidian.Net;
using Obsidian.Net.Actions.PlayerInfo;
using Obsidian.Net.ClientHandlers;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Handshaking;
using Obsidian.Net.Packets.Login;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Status;
using Obsidian.Services;
using Obsidian.Utilities.Mojang;
using Obsidian.WorldData;
using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks.Dataflow;

namespace Obsidian;

public sealed class Client : IDisposable
{
    /// <summary>
    /// The max amount of bytes that can be sent to the client before compression is required.
    /// </summary>
    private const int CompressionThreshold = 256;

    /// <summary>
    /// The player's entity id.
    /// </summary>
    internal int id;

    /// <summary>
    /// How many <see cref="KeepAlivePacket"/>s the client has missed.
    /// </summary>
    internal List<long> missedKeepAlives;

    /// <summary>
    /// The client's ping in milliseconds.
    /// </summary>
    internal int ping;

    /// <summary>
    /// The public key/signature data received from mojang.
    /// </summary>
    internal SignatureData? signatureData;

    /// <summary>
    /// Used for signing chat messages.
    /// </summary>
    internal MessageSigningData? messageSigningData;

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
    private byte[]? randomToken;

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

    /// <summary>
    /// Which packets are in queue to be sent to the client.
    /// </summary>
    private readonly BufferBlock<IClientboundPacket> packetQueue;

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

    /// <summary>
    /// Whether the stream has encryption enabled. This can be set to false when the client is connecting through LAN or when the server is in offline mode.
    /// </summary>
    public bool EncryptionEnabled { get; private set; }

    /// <summary>
    /// Which state of the protocol the client is currently in.
    /// </summary>
    public ClientState State { get; internal set; } = ClientState.Handshaking;

    /// <summary>
    /// Which chunks the player should have loaded around them.
    /// </summary>
    public ConcurrentHashSet<(int X, int Z)> LoadedChunks { get; internal set; }

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
    public string? Brand { get; set; }

    public Client(ConnectionContext connectionContext,
        ILoggerFactory loggerFactory, IUserCache playerCache,
        Server server)
    {
        this.connectionContext = connectionContext;
        this.loggerFactory = loggerFactory;
        this.server = server;
        this.userCache = playerCache;
        this.Logger = loggerFactory.CreateLogger("ConnectionHandler");

        LoadedChunks = [];
        packetCryptography = new();
        this.handlers = new Dictionary<ClientState, ClientHandler>()
        {
            { ClientState.Login, new LoginClientHandler { Client = this } },
            { ClientState.Configuration, new ConfigurationClientHandler { Client = this } },
            { ClientState.Play, new PlayClientHandler { Client = this } }
        }.ToFrozenDictionary();

        networkStream = new(connectionContext.Transport);
        minecraftStream = new(networkStream);

        missedKeepAlives = [];
        var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };
        var blockOptions = new ExecutionDataflowBlockOptions { CancellationToken = cancellationSource.Token, EnsureOrdered = true };
        var sendPacketBlock = new ActionBlock<IClientboundPacket>(packet =>
        {
            if (connectionContext.IsConnected())
                SendPacket(packet);
        }, blockOptions);

        packetQueue = new BufferBlock<IClientboundPacket>(blockOptions);
        _ = packetQueue.LinkTo(sendPacketBlock, linkOptions);
    }

    private async ValueTask<PacketData> GetNextPacketAsync()
    {
        var length = await minecraftStream.ReadVarIntAsync();
        var receivedData = ArrayPool<byte>.Shared.Rent(length);

        _ = await minecraftStream.ReadAsync(receivedData.AsMemory(0, length));


        byte[] packetData = default!;
        int packetId = default!;

        var error = false;
        await using (var packetStream = new MinecraftStream(receivedData))
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

    public async Task StartConnectionAsync()
    {
        while (!cancellationSource.IsCancellationRequested && connectionContext.IsConnected())
        {
            using var packetData = await GetNextPacketAsync();

            if (State == ClientState.Play && packetData.Data.Length < 1)
                Disconnect();

            switch (State)
            {
                case ClientState.Status: // Server ping/list
                    if (packetData.Id == 0x00)
                    {
                        var status = new ServerStatus(this.server);

                        _ = await this.server.EventDispatcher.ExecuteEventAsync(new ServerStatusRequestEventArgs(this.server, status));

                        this.SendPacket(new RequestResponse(status));
                    }
                    else if (packetData.Id == 0x01)
                    {
                        this.SendPacket(PingPong.Deserialize(packetData.Data));
                        this.Disconnect();
                    }
                    break;

                case ClientState.Handshaking:
                    if (packetData.Id == 0x00)
                    {
                        var handshake = Handshake.Deserialize(packetData.Data);
                        await handshake.HandleAsync(this);
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
                        return;

                    await this.HandlePacketAsync(packetData);
                    break;
                case ClientState.Play:
                    Debug.Assert(Player is not null);

                    result = await this.server.EventDispatcher.ExecuteEventAsync(new PacketReceivedEventArgs(Player, this.server, packetData.Id, packetData.Data));

                    if (result == EventResult.Cancelled)
                        return;

                    await this.HandlePacketAsync(packetData);

                    break;
                case ClientState.Closed:
                default:
                    break;
            }
        }

        Logger.LogInformation("Disconnected client");

        if (State == ClientState.Play)
        {
            Debug.Assert(Player is not null);
            await this.server.EventDispatcher.ExecuteEventAsync(new PlayerLeaveEventArgs(Player, this.server, DateTimeOffset.Now));
        }

        Disconnected?.Invoke(this);
        this.Dispose();//Dispose client after
    }

    internal void ThrowIfInvalidEncryptionRequest()
    {
        if (this.Player is null)
            throw new InvalidOperationException("Received Encryption Response before sending Login Start.");

        if (this.randomToken is null)
            throw new InvalidOperationException("Received Encryption Response before sending Encryption Request.");
    }

    private async ValueTask<bool> HandlePacketAsync(PacketData packetData) => await this.handlers[this.State].HandleAsync(packetData);


    public async ValueTask<bool> TrySetCachedProfileAsync(string username)
    {
        ArgumentNullException.ThrowIfNull(username, nameof(username));

        this.profile = await this.userCache.GetCachedUserFromNameAsync(username);

        if (this.profile is null)
        {
            await DisconnectAsync("Account not found in the Mojang database");

            return false;
        }
        else if (this.server.IsWhitedlisted(this.profile.Uuid))
        {
            await DisconnectAsync("You are not whitelisted on this server\nContact server administrator");

            return false;
        }

        this.InitializeId();

        return true;
    }

    public async Task<bool> TryValidateEncryptionResponseAsync(byte[] sharedSecret, byte[] verifyToken)
    {
        this.sharedKey = packetCryptography.Decrypt(sharedSecret);

        var decryptedToken = packetCryptography.Decrypt(verifyToken);

        if (!decryptedToken.SequenceEqual(this.randomToken!))
        {
            await this.DisconnectAsync("Invalid token...");
            return false;
        }

        var serverId = sharedKey.Concat(packetCryptography.PublicKey).MinecraftShaDigest();
        if (await this.userCache.HasJoinedAsync(this.Player!.Username, serverId) is not MojangProfile user)
        {
            this.Logger.LogWarning("Failed to auth {Username}", this.Player.Username);
            await this.DisconnectAsync("Unable to authenticate...");
            return false;
        }

        this.Player.SkinProperties = user.Properties!;
        this.EncryptionEnabled = true;
        this.minecraftStream = new EncryptedMinecraftStream(networkStream, sharedKey);

        this.SendPacket(new LoginSuccess(Player.Uuid, Player.Username)
        {
            SkinProperties = this.Player.SkinProperties,
        });

        return true;
    }

    public void Initialize(World world)
    {
        if (this.profile == null)
            throw new UnreachableException("Profile was not set or is null.");

        this.Player = new(this.profile.Uuid, this.profile.Name, this, world);

        this.packetCryptography.GenerateKeyPair();

        var (publicKey, randomToken) = this.packetCryptography.GeneratePublicKeyAndToken();

        this.randomToken = randomToken;

        this.SendPacket(new EncryptionRequest
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

        this.SendPacket(new LoginSuccess(Player.Uuid, Player.Username)
        {
            SkinProperties = this.Player.SkinProperties,
        });
    }

    private void InitializeId()
    {
        this.id = Server.GetNextEntityId();
        this.Logger = this.loggerFactory.CreateLogger($"Client({this.id})");
    }

    // TODO fix compression now????
    private void SetCompression()
    {
        SendPacket(new SetCompression(CompressionThreshold));
        compressionEnabled = true;
        Logger.LogDebug("Compression has been enabled.");
    }

    #region Packet sending
    internal async Task SendInfoAsync()
    {
        if (Player is null)
            throw new UnreachableException("Player is null, which means the client has not yet logged in.");

        await QueuePacketAsync(new SetDefaultSpawnPositionPacket(Player.world.LevelData.SpawnPosition));
        await QueuePacketAsync(new UpdateTimePacket(Player!.world.LevelData.Time, Player.world.LevelData.DayTime));
        await QueuePacketAsync(new GameEventPacket(Player!.world.LevelData.Raining ? ChangeGameStateReason.BeginRaining : ChangeGameStateReason.EndRaining));
        await QueuePacketAsync(new SetContainerContentPacket(0, Player.Inventory.ToList())
        {
            StateId = Player.Inventory.StateId++,
            CarriedItem = Player.GetHeldItem(),
        });

        await QueuePacketAsync(new SetEntityMetadataPacket
        {
            EntityId = this.Player.EntityId,
            Entity = this.Player
        });
    }

    internal async Task DisconnectAsync(ChatMessage reason) => await this.QueuePacketAsync(new DisconnectPacket(reason, State));

    internal Task RemovePlayerFromListAsync(IPlayer player) => QueuePacketAsync(new PlayerInfoRemovePacket
    {
        UUIDs = [player.Uuid]
    });

    internal async Task AddPlayerToListAsync(IPlayer player)
    {
        if (player is null)
        {
            throw new ArgumentNullException(nameof(player));
        }
        else if (Player is null)
        {
            throw new InvalidOperationException("Player is null, which means the client has not yet logged in.");
        }

        var addAction = new AddPlayerInfoAction
        {
            Name = player.Username,
        };

        if (this.server.Configuration.OnlineMode)
            addAction.Properties.AddRange(player.SkinProperties);

        var list = new List<InfoAction>()
        {
            addAction,
            new UpdatePingInfoAction(player.Ping),
            new UpdateListedInfoAction(player.ClientInformation.AllowServerListings),
        };

        await QueuePacketAsync(new PlayerInfoUpdatePacket(new Dictionary<Guid, List<InfoAction>>()
        {
            { player.Uuid, list }
        }));
    }

    internal async Task SendPlayerInfoAsync()
    {
        if (Player is null)
        {
            throw new InvalidOperationException("Player is null, which means the client has not yet logged in.");
        }

        var dict = new Dictionary<Guid, List<InfoAction>>();
        foreach (var player in this.server.OnlinePlayers.Values)
        {
            var addPlayerInforAction = new AddPlayerInfoAction()
            {
                Name = player.Username,
            };

            if (this.server.Configuration.OnlineMode)
                addPlayerInforAction.Properties.AddRange(player.SkinProperties);

            var list = new List<InfoAction>
            {
                addPlayerInforAction,
                new UpdateListedInfoAction(player.ClientInformation.AllowServerListings),
                new UpdateDisplayNameInfoAction(player.Username),
                new UpdatePingInfoAction(player.Ping)
            };

            dict.Add(player.Uuid, list);
        }

        await QueuePacketAsync(new PlayerInfoUpdatePacket(dict));
        await QueuePacketAsync(new PlayerAbilitiesPacket(true)
        {
            Abilities = Player.Abilities
        });
    }

    internal void SendPacket(IClientboundPacket packet)
    {
        try
        {
            if (!compressionEnabled)
            {
                packet.Serialize(minecraftStream);
            }
            else
            {
                //await packet.WriteCompressedAsync(minecraftStream, compressionThreshold);//TODO
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
            Logger.LogDebug(e, "Sending packet {PacketId} failed", packet.Id);
        }
    }

    internal async Task QueuePacketAsync(IClientboundPacket packet)
    {
        var args = new QueuePacketEventArgs(this.server, this, packet);

        var result = await this.server.EventDispatcher.ExecuteEventAsync(args);
        if (result == EventResult.Cancelled)
        {
            Logger.LogDebug("Packet {PacketId} was sent to the queue, however an event handler has cancelled it.", args.Packet.Id);
        }
        else
        {
            _ = await packetQueue.SendAsync(packet);
        }
    }

    internal async Task SendChunkAsync(Chunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        await QueuePacketAsync(new ChunkDataAndUpdateLightPacket(chunk));
    }
    internal Task UnloadChunkAsync(int x, int z) => LoadedChunks.Contains((x, z)) ? QueuePacketAsync(new UnloadChunkPacket(x, z)) : Task.CompletedTask;

    #endregion Packet sending

    internal void Disconnect()
    {
        cancellationSource.Cancel();
        Disconnected?.Invoke(this);

        this.Dispose();
    }

    internal void SetState(ClientState state) => this.State = state;

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
