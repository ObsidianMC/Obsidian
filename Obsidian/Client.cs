using Microsoft.Extensions.Logging;
using Obsidian.API.Events;
using Obsidian.Concurrency;
using Obsidian.Entities;
using Obsidian.Events.EventArgs;
using Obsidian.Net;
using Obsidian.Net.Actions.PlayerInfo;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Handshaking;
using Obsidian.Net.Packets.Login;
using Obsidian.Net.Packets.Play;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Net.Packets.Status;
using Obsidian.Registries;
using Obsidian.Utilities.Mojang;
using Obsidian.WorldData;
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
    private CachedUser? cachedUser;

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
    private readonly ClientHandler handler;

    /// <summary>
    /// The base network stream used by the <see cref="minecraftStream"/>.
    /// </summary>
    private readonly NetworkStream networkStream;

    /// <summary>
    /// Used to continuously send and receive encrypted packets from the client.
    /// </summary>
    private readonly PacketCryptography packetCryptography;

    /// <summary>
    /// The socket associated with the <see cref="networkStream"/>.
    /// </summary>
    private readonly Socket socket;

    /// <summary>
    /// The current server configuration.
    /// </summary>
    private readonly ServerConfiguration config;

    /// <summary>
    /// Whether the stream has encryption enabled. This can be set to false when the client is connecting through LAN or when the server is in offline mode.
    /// </summary>
    public bool EncryptionEnabled { get; private set; }

    /// <summary>
    /// Which state of the protocol the client is currently in.
    /// </summary>
    public ClientState State { get; private set; } = ClientState.Handshaking;

    /// <summary>
    /// Which chunks the player should have loaded around them.
    /// </summary>
    public ConcurrentHashSet<(int X, int Z)> LoadedChunks { get; internal set; }

    /// <summary>
    /// The client's ip and port used to establish this connection.
    /// </summary>
    public EndPoint? RemoteEndPoint => socket.RemoteEndPoint;

    /// <summary>
    /// Executed when the client disconnects.
    /// </summary>
    public event Action<Client>? Disconnected;

    /// <summary>
    /// Used to log actions caused by the client.
    /// </summary>
    public ILogger Logger => Server.Logger;

    /// <summary>
    /// The player that the client is logged in as.
    /// </summary>
    public Player? Player { get; private set; }

    /// <summary>
    /// The server that the client is connected to.
    /// </summary>
    public Server Server { get; private set; }

    /// <summary>
    /// The client brand. This is the name that the client used to identify itself (Fabric, Forge, Quilt, etc.)
    /// </summary>
    public string? Brand { get; set; }

    public Client(Socket socket, ServerConfiguration config, int playerId, Server originServer)
    {
        this.socket = socket;
        this.config = config;
        id = playerId;
        Server = originServer;

        LoadedChunks = new();
        packetCryptography = new();
        handler = new(config);
        networkStream = new(socket);
        minecraftStream = new(networkStream);

        missedKeepAlives = new List<long>();
        var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };
        var blockOptions = new ExecutionDataflowBlockOptions { CancellationToken = cancellationSource.Token, EnsureOrdered = true };
        var sendPacketBlock = new ActionBlock<IClientboundPacket>(packet =>
        {
            if (socket.Connected)
                SendPacket(packet);
        }, blockOptions);

        packetQueue = new BufferBlock<IClientboundPacket>(blockOptions);
        _ = packetQueue.LinkTo(sendPacketBlock, linkOptions);

        handler.RegisterHandlers();
    }

    private async Task<(int id, byte[] data)> GetNextPacketAsync()
    {
        var length = await minecraftStream.ReadVarIntAsync();
        var receivedData = new byte[length];

        _ = await minecraftStream.ReadAsync(receivedData.AsMemory(0, length));

        var packetId = 0;
        var packetData = Array.Empty<byte>();

        using (var packetStream = new MinecraftStream(receivedData))
        {
            try
            {
                packetId = await packetStream.ReadVarIntAsync();
                var arlen = 0;

                if (length - packetId.GetVarIntLength() > -1)
                    arlen = length - packetId.GetVarIntLength();

                packetData = new byte[arlen];
                _ = await packetStream.ReadAsync(packetData.AsMemory(0, packetData.Length));
            }
            catch
            {
                throw;
            }
        }

        return (packetId, packetData);
    }

    public async Task StartConnectionAsync()
    {
        while (!cancellationSource.IsCancellationRequested && socket.Connected)
        {
            (var id, var data) = await GetNextPacketAsync();

            if (State == ClientState.Play && data.Length < 1)
                Disconnect();

            switch (State)
            {
                case ClientState.Status: // Server ping/list
                    if (id == 0x00)
                    {
                        await HandleServerStatusRequestAsync();
                    }
                    else if (id == 0x01)
                    {
                        await HandlePingPongAsync(data);
                    }
                    break;

                case ClientState.Handshaking:
                    if (id == 0x00)
                    {
                        await HandleHandshakeAsync(data);
                    }
                    else
                    {
                        // Handle legacy ping
                    }
                    break;

                case ClientState.Login:
                    switch (id)
                    {
                        case 0x00:
                        {
                            if (this.Server.Config.CanThrottle)
                            {
                                string ip = ((IPEndPoint)socket.RemoteEndPoint!).Address.ToString();

                                if (Server.throttler.TryGetValue(ip, out var timeLeft))
                                {
                                    if (DateTimeOffset.UtcNow < timeLeft)
                                    {
                                        this.Logger.LogDebug("{ip} has been throttled for reconnecting too fast.", ip);
                                        await this.DisconnectAsync("Connection Throttled! Please wait before reconnecting.");
                                        this.Disconnect();
                                    }
                                }
                                else
                                {
                                    Server.throttler.TryAdd(ip, DateTimeOffset.UtcNow.AddMilliseconds(this.Server.Config.ConnectionThrottle));
                                }
                            }

                            await HandleLoginStartAsync(data);
                            break;
                        }
                        case 0x01:
                            await HandleEncryptionResponseAsync(data);
                            break;
                        case 0x02:
                            // Login Plugin Response
                            break;

                        default:
                            Logger.LogError("Client in state Login tried to send an unimplemented packet. Forcing it to disconnect.");
                            await DisconnectAsync("Unknown Packet Id.");
                            break;
                    }
                    break;

                case ClientState.Play:
                    Debug.Assert(Player is not null);
                    var packetReceivedEventArgs = new PacketReceivedEventArgs(Player, id, data);
                    await Server.Events.InvokePacketReceivedAsync(packetReceivedEventArgs);

                    if (!packetReceivedEventArgs.IsCancelled)
                    {
                        await handler.HandlePlayPackets(id, data, this);
                    }
                    break;
                case ClientState.Closed:
                default:
                    break;
            }
        }

        Logger.LogInformation($"Disconnected client");

        if (State == ClientState.Play)
        {
            Debug.Assert(Player is not null);
            await Server.Events.InvokePlayerLeaveAsync(new PlayerLeaveEventArgs(Player, DateTimeOffset.Now));
        }

        Disconnected?.Invoke(this);
        this.Dispose();//Dispose client after
    }

    private async Task HandleServerStatusRequestAsync()
    {
        var status = new ServerStatus(Server);

        _ = await Server.Events.InvokeServerStatusRequest(new ServerStatusRequestEventArgs(Server, status));

        SendPacket(new RequestResponse(status));
    }

    private Task HandlePingPongAsync(byte[] data)
    {
        var pong = PingPong.Deserialize(data);
        SendPacket(pong); // TODO make sure that the packet is fully sent before disconnecting
        Disconnect();
        return Task.CompletedTask;
    }

    private async Task HandleHandshakeAsync(byte[] data)
    {
        var handshake = Handshake.Deserialize(data);
        var nextState = handshake.NextState;

        if (nextState == ClientState.Login)
        {
            if ((int)handshake.Version > (int)Server.Protocol)
            {
                await DisconnectAsync($"Outdated server! I'm still on {Server.Protocol.GetDescription()}.");
            }
            else if ((int)handshake.Version < (int)Server.Protocol)
            {
                await DisconnectAsync($"Outdated client! Please use {Server.Protocol.GetDescription()}.");
            }
        }
        else if (nextState is not ClientState.Status or ClientState.Login or ClientState.Handshaking)
        {
            Logger.LogWarning("Client sent unexpected state ({RedText}{ClientState}{WhiteText}), forcing it to disconnect.", ChatColor.Red, nextState, ChatColor.White);
            await DisconnectAsync($"Invalid client state! Expected Status or Login, received {nextState}.");
        }

        State = nextState == ClientState.Login && handshake.Version != Server.Protocol ? ClientState.Closed : nextState;


        var versionDesc = handshake.Version.GetDescription();
        if (versionDesc is null)
            return;//No need to log if version description is null

        Logger.LogInformation("Handshaking with client (protocol: {YellowText}{VersionDescription}{WhiteText} [{YellowText}{Version}{WhiteText}], server: {YellowText}{ServerAddress}:{ServerPort}{WhiteText})", ChatColor.Yellow, versionDesc, ChatColor.White, ChatColor.Yellow, handshake.Version, ChatColor.White, ChatColor.Yellow, handshake.ServerAddress, handshake.ServerPort, ChatColor.White);
    }

    private async Task HandleLoginStartAsync(byte[] data)
    {
        var loginStart = LoginStart.Deserialize(data);
        var username = config.MulitplayerDebugMode ? $"Player{Globals.Random.Next(1, 999)}" : loginStart.Username;
        var world = (World)Server.DefaultWorld;

        Logger.LogDebug("Received login request from user {Username}", loginStart.Username);
        await Server.DisconnectIfConnectedAsync(username);

        if (config.OnlineMode)
        {
            cachedUser = await UserCache.GetUserFromUuidAsync(loginStart.PlayerUuid ?? throw new NullReferenceException(nameof(loginStart.PlayerUuid)));

            if (cachedUser is null)
            {
                await DisconnectAsync("Account not found in the Mojang database");
                return;
            }
            else if (config.WhitelistEnabled && !config.Whitelisted.Any(x => x.Id == cachedUser.Id))
            {
                await DisconnectAsync("You are not whitelisted on this server\nContact server administrator");
                return;
            }

            Player = new Player(loginStart.PlayerUuid ?? this.cachedUser.Id, loginStart.Username, this, world);

            packetCryptography.GenerateKeyPair();

            var (publicKey, randomToken) = packetCryptography.GeneratePublicKeyAndToken();

            this.randomToken = randomToken;

            SendPacket(new EncryptionRequest
            {
                PublicKey = publicKey,
                VerifyToken = randomToken
            });
        }
        else if (config.WhitelistEnabled && !config.Whitelisted.Any(x => x.Name == username))
        {
            await DisconnectAsync("You are not whitelisted on this server\nContact server administrator");
        }
        else
        {
            Player = new Player(GuidHelper.FromStringHash($"OfflinePlayer:{username}"), username, this, world);

            // TODO: Compression, .net 6 (see method below)
            //await this.SetCompression();
            await ConnectAsync();
        }
    }

    private async Task HandleEncryptionResponseAsync(byte[] data)
    {
        if (Player is null)
        {
            throw new InvalidOperationException("Received Encryption Response before sending Login Start.");
        }
        else if (randomToken is null)
        {
            throw new InvalidOperationException("Received Encryption Response before sending Encryption Request.");
        }

        // Decrypt the shared secret and verify the token
        var encryptionResponse = EncryptionResponse.Deserialize(data);

        sharedKey = packetCryptography.Decrypt(encryptionResponse.SharedSecret);

        var decryptedToken = packetCryptography.Decrypt(encryptionResponse.VerifyToken);

        if (!decryptedToken.SequenceEqual(randomToken))
        {
            await DisconnectAsync("Invalid token...");
            return;
        }

        var serverId = sharedKey.Concat(packetCryptography.PublicKey).MinecraftShaDigest();
        if (await UserCache.HasJoinedAsync(Player.Username, serverId) is not MojangUser user)
        {
            Logger.LogWarning("Failed to auth {Username}", Player.Username);
            await DisconnectAsync("Unable to authenticate...");
            return;
        }

        this.Player.SkinProperties = user.Properties;
        EncryptionEnabled = true;
        minecraftStream = new EncryptedMinecraftStream(networkStream, sharedKey);

        // TODO: Fix compression
        //await this.SetCompression();
        await ConnectAsync();
    }

    // TODO fix compression (.net 6)
    private void SetCompression()
    {
        SendPacket(new SetCompression(CompressionThreshold));
        compressionEnabled = true;
        Logger.LogDebug("Compression has been enabled.");
    }

    private async Task ConnectAsync()
    {
        if (Player is null)
        {
            throw new InvalidOperationException("Player is null, which means the client has not yet logged in.");
        }

        await QueuePacketAsync(new LoginSuccess(Player.Uuid, Player.Username)
        {
            SkinProperties = this.Player.SkinProperties,
        });

        Logger.LogDebug("Sent Login success to user {Username} {UUID}", Player.Username, Player.Uuid);

        State = ClientState.Play;
        await Player.LoadAsync();
        if (!Server.OnlinePlayers.TryAdd(Player.Uuid, Player))
        {
            Logger.LogError("Failed to add player {Username} to online players. Undefined behavior ahead!", Player.Username);
        }

        if (!CodecRegistry.TryGetDimension(Player.World.DimensionName, out var codec) || !CodecRegistry.TryGetDimension("minecraft:overworld", out codec))
            throw new UnreachableException("Failed to retrieve proper dimension for player.");

        await QueuePacketAsync(new LoginPacket
        {
            EntityId = id,
            Gamemode = Player.Gamemode,
            DimensionNames = CodecRegistry.Dimensions.All.Keys.ToList(),
            Codecs = new(),
            DimensionType = codec.Name,
            DimensionName = codec.Name,
            HashedSeed = 0,
            ReducedDebugInfo = false,
            EnableRespawnScreen = true,
            Flat = false
        });

        await SendServerBrand();
        await QueuePacketAsync(UpdateTagsPacket.FromRegistry);
        await SendCommandsAsync();

        await QueuePacketAsync(UpdateRecipesPacket.FromRegistry);

        await QueuePacketAsync(new UpdateRecipeBookPacket
        {
            Action = UnlockRecipeAction.Init,
            FirstRecipeIds = RecipesRegistry.Recipes.Keys.ToList(),
            SecondRecipeIds = RecipesRegistry.Recipes.Keys.ToList()
        });

        await SendPlayerListDecoration();
        await SendPlayerInfoAsync();
        while (!await Player.UpdateChunksAsync(distance: 7)) { Thread.Sleep(1); }
        await SendInfoAsync();
        await Server.Events.InvokePlayerJoinAsync(new PlayerJoinEventArgs(Player, DateTimeOffset.Now));
    }

    #region Packet sending
    internal async Task SendInfoAsync()
    {
        if (Player is null)
            throw new UnreachableException("Player is null, which means the client has not yet logged in.");

        Player.TeleportId = Globals.Random.Next(0, 999);
        await QueuePacketAsync(new SetDefaultSpawnPositionPacket(Player.World.LevelData.SpawnPosition));
        await QueuePacketAsync(new SynchronizePlayerPositionPacket
        {
            Position = Player.Position,
            Yaw = 0,
            Pitch = 0,
            Flags = PositionFlags.None,
            TeleportId = Player.TeleportId
        });

        await SendTimeUpdateAsync();
        await SendWeatherUpdateAsync();
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

    internal Task DisconnectAsync(ChatMessage reason) => Task.Run(() => SendPacket(new DisconnectPacket(reason, State)));
    internal Task SendTimeUpdateAsync() => QueuePacketAsync(new UpdateTimePacket(Player!.World.LevelData.Time, Player.World.LevelData.DayTime));
    internal Task SendWeatherUpdateAsync() => QueuePacketAsync(new GameEventPacket(Player!.World.LevelData.Raining ? ChangeGameStateReason.BeginRaining : ChangeGameStateReason.EndRaining));

    internal void HandleKeepAlive(KeepAlivePacket keepAlive)
    {
        if (!missedKeepAlives.Contains(keepAlive.KeepAliveId))
        {
            Server.Logger.LogWarning($"Received invalid KeepAlive from {Player.Username}?? Naughty???? ({Player.Uuid})");
            DisconnectAsync(ChatMessage.Simple("Kicked for invalid KeepAlive."));
            return;
        }

        // from now on we know this keepalive is VALID and WITHIN BOUNDS
        decimal ping = DateTimeOffset.Now.ToUnixTimeMilliseconds() - keepAlive.KeepAliveId;
        ping = Math.Min(int.MaxValue, ping); // convert within integer bounds
        ping = Math.Max(0, ping); // negative ping is impossible.

        this.ping = (int)ping;
        Logger.LogDebug($"Valid KeepAlive ({keepAlive.KeepAliveId}) handled from {Player.Username} ({Player.Uuid})");
        // KeepAlive is handled.
        missedKeepAlives.Remove(keepAlive.KeepAliveId);
    }

    internal void SendKeepAlive(DateTimeOffset time)
    {
        long keepAliveId = time.ToUnixTimeMilliseconds();
        // first, check if there's any KeepAlives that are older than 30 seconds
        if (missedKeepAlives.Any(x => keepAliveId - x > config.KeepAliveTimeoutInterval))
        {
            // kick player, failed to respond within 30s
            cancellationSource.Cancel();
            return;
        }

        Logger.LogDebug($"Doing KeepAlive ({keepAliveId}) with {Player.Username} ({Player.Uuid})");
        // now that all is fine and dandy, we'd be fine to enqueue the new keepalive
        SendPacket(new KeepAlivePacket(keepAliveId));
        missedKeepAlives.Add(keepAliveId);

        // TODO: reimplement this? probably in KeepAlivePacket:HandleAsync ⬇️

        //// Sending ping change in background
        //await Task.Run(async delegate ()
        //{
        //    foreach (Client client in OriginServer.Clients.Where(c => c.IsPlaying))
        //    {
        //        await PacketHandler.CreateAsync(new PlayerInfo(2, new List<PlayerInfoAction>()
        //        {
        //            new PlayerInfoUpdatePingAction()
        //            {
        //                Ping = this.Ping
        //            }
        //        }), this.MinecraftStream);
        //    }
        //}).ConfigureAwait(false);
    }

    internal Task SendCommandsAsync() => QueuePacketAsync(CommandsRegistry.Packet);

    internal Task RemovePlayerFromListAsync(IPlayer player) => QueuePacketAsync(new PlayerInfoRemovePacket
    {
        UUIDs = new() { player.Uuid }
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

        if (config.OnlineMode)
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
        foreach (var player in Server.OnlinePlayers.Values)
        {
            var addPlayerInforAction = new AddPlayerInfoAction()
            {
                Name = player.Username,
            };

            if (config.OnlineMode)
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
            if (!socket.Connected)
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
        var args = await Server.Events.InvokeQueuePacketAsync(new QueuePacketEventArgs(this, packet));
        if (args.IsCancelled)
        {
            Logger.LogDebug("Packet {PacketId} was sent to the queue, however an event handler registered in {Name} has cancelled it.", args.Packet.Id, nameof(Server.Events));
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

    private async Task SendServerBrand()
    {
        using var stream = new MinecraftStream();
        await stream.WriteStringAsync(Server.Brand);
        await QueuePacketAsync(new PluginMessagePacket("minecraft:brand", stream.ToArray()));
        Logger.LogDebug("Sent server brand.");
    }

    private async Task SendPlayerListDecoration()
    {
        var header = string.IsNullOrWhiteSpace(Server.Config.Header) ? null : ChatMessage.Simple(Server.Config.Header);
        var footer = string.IsNullOrWhiteSpace(Server.Config.Footer) ? null : ChatMessage.Simple(Server.Config.Footer);

        await QueuePacketAsync(new SetTabListHeaderAndFooterPacket(header, footer));
        Logger.LogDebug("Sent player list decoration");
    }
    #endregion Packet sending

    internal void Disconnect()
    {
        cancellationSource.Cancel();
        Disconnected?.Invoke(this);

        this.Dispose();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        minecraftStream.Dispose();
        socket.Dispose();
        cancellationSource?.Dispose();

        GC.SuppressFinalize(this);
    }
}
