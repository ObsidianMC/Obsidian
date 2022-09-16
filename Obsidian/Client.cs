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
using Obsidian.Utilities.Mojang;
using Obsidian.Utilities.Registry;
using Obsidian.WorldData;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks.Dataflow;

namespace Obsidian;

public sealed class Client : IDisposable
{
    public event Action<Client>? Disconnected;

    private byte[]? randomToken;
    private byte[]? sharedKey;

    private readonly BufferBlock<IClientboundPacket> packetQueue;

    private readonly PacketCryptography packetCryptography;

    private readonly ClientHandler handler;

    private MinecraftStream minecraftStream;
    private readonly NetworkStream socketStream;

    private ServerConfiguration config;

    private bool disposed;
    private bool compressionEnabled;

    private MojangUser cachedMojangUser;

    public bool EncryptionEnabled { get; private set; }

    private const int CompressionThreshold = 256;

    private readonly Socket socket;

    internal int ping;
    internal int missedKeepalives;
    internal int id;

    /// <summary>
    /// The client brand.
    /// </summary>
    public string? Brand { get; set; }

    public ClientInformationPacket? ClientSettings { get; internal set; }
    public EndPoint? RemoteEndPoint => socket.RemoteEndPoint;

    private readonly CancellationTokenSource cancellationSource = new();

    public ClientState State { get; private set; } = ClientState.Handshaking;

    public Server Server { get; private set; }
    public Player? Player { get; private set; }

    public ILogger Logger => Server.Logger;

    public ConcurrentHashSet<(int X, int Z)> LoadedChunks { get; internal set; }

    public Client(Socket socket, ServerConfiguration config, int playerId, Server originServer)
    {
        this.socket = socket;
        this.config = config;
        id = playerId;
        packetCryptography = new PacketCryptography();
        Server = originServer;
        LoadedChunks = new();
        handler = new ClientHandler(config);

        socketStream = new NetworkStream(socket);
        minecraftStream = new MinecraftStream(socketStream);

        var blockOptions = new ExecutionDataflowBlockOptions { CancellationToken = cancellationSource.Token, EnsureOrdered = true };
        packetQueue = new BufferBlock<IClientboundPacket>(blockOptions);
        var sendPacketBlock = new ActionBlock<IClientboundPacket>(packet =>
        {
            if (socket.Connected)
                SendPacket(packet);
        },
        blockOptions);

        var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };
        packetQueue.LinkTo(sendPacketBlock, linkOptions);

        handler.RegisterHandlers();
    }

    private async Task<(int id, byte[] data)> GetNextPacketAsync()
    {
        int length = await minecraftStream.ReadVarIntAsync();
        byte[] receivedData = new byte[length];

        await minecraftStream.ReadAsync(receivedData.AsMemory(0, length));

        int packetId = 0;
        byte[] packetData = Array.Empty<byte>();

        using (var packetStream = new MinecraftStream(receivedData))
        {
            try
            {
                packetId = await packetStream.ReadVarIntAsync();
                int arlen = 0;

                if (length - packetId.GetVarIntLength() > -1)
                    arlen = length - packetId.GetVarIntLength();

                packetData = new byte[arlen];
                await packetStream.ReadAsync(packetData.AsMemory(0, packetData.Length));
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
            (int id, byte[] data) = await GetNextPacketAsync();

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
                            await HandleLoginStartAsync(data);
                            break;

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

                    if (!packetReceivedEventArgs.Cancel)
                    {
                        await handler.HandlePlayPackets(id, data, this);
                    }
                    break;
            }
        }

        Logger.LogInformation($"Disconnected client");

        if (State == ClientState.Play)
        {
            Debug.Assert(Player is not null);
            await Server.Events.InvokePlayerLeaveAsync(new PlayerLeaveEventArgs(Player, DateTimeOffset.Now));
        }

        if (socket.Connected)
        {
            socket.Close();

            if (Player is not null)
                Server.OnlinePlayers.TryRemove(Player.Uuid, out var _);

            Disconnected?.Invoke(this);
        }
    }

    private async Task HandleServerStatusRequestAsync()
    {
        var status = new ServerStatus(Server);

        await Server.Events.InvokeServerStatusRequest(new ServerStatusRequestEventArgs(Server, status));

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

        if (nextState != ClientState.Status && nextState != ClientState.Login)
        {
            Logger.LogDebug($"Client sent unexpected state ({ChatColor.Red}{(int)nextState}{ChatColor.White}), forcing it to disconnect");
            await DisconnectAsync("you seem suspicious");
        }

        if (nextState == ClientState.Login)
        {
            if ((int)handshake.Version > (int)Server.Protocol)
            {
                await DisconnectAsync($"Outdated server! I'm still on {Server.Protocol.GetDescription()}.");
            }

            if ((int)handshake.Version < (int)Server.Protocol)
            {
                await DisconnectAsync($"Outdated client! Please use {Server.Protocol.GetDescription()}.");
            }
        }

        State = nextState == ClientState.Login && ((int)handshake.Version != (int)Server.Protocol) ? ClientState.Closed : nextState;
        Logger.LogInformation($"Handshaking with client (protocol: {ChatColor.Yellow}{handshake.Version.GetDescription() ?? "UNSUPPORTED"} {ChatColor.White}[{ChatColor.Yellow}{(int)handshake.Version}{ChatColor.White}], server: {ChatColor.Yellow}{handshake.ServerAddress}:{handshake.ServerPort}{ChatColor.White})");
    }

    private async Task HandleLoginStartAsync(byte[] data)
    {
        var loginStart = LoginStart.Deserialize(data);

        string username = config.MulitplayerDebugMode ? $"Player{Globals.Random.Next(1, 999)}" : loginStart.Username;

        Logger.LogDebug($"Received login request from user {loginStart.Username}");

        await Server.DisconnectIfConnectedAsync(username);

        var world = (World)Server.DefaultWorld;
        if (config.OnlineMode)
        {
            this.cachedMojangUser = await MinecraftAPI.GetUserAndSkinAsync(loginStart.Username);

            if (this.cachedMojangUser is null)
            {
                await DisconnectAsync("Account not found in the Mojang database");
                return;
            }

            if (config.WhitelistEnabled)
            {
                var wlEntry = config.UserWhitelist.FirstOrDefault(x => x.UUID == this.cachedMojangUser.Id);

                if (wlEntry is null)
                {
                    await DisconnectAsync("You are not whitelisted on this server\nContact server administrator");
                    return;
                }

            }

            Player = new Player(Guid.Parse(this.cachedMojangUser.Id), loginStart.Username, this, world);

            packetCryptography.GenerateKeyPair();

            var values = packetCryptography.GeneratePublicKeyAndToken();

            randomToken = values.randomToken;

            SendPacket(new EncryptionRequest(values.publicKey, randomToken));
        }
        else if (config.WhitelistEnabled && !config.UserWhitelist.Any(x => x.Nickname == username))
        {
            await DisconnectAsync("You are not whitelisted on this server\nContact server administrator");
        }
        else
        {
            Player = new Player(GuidHelper.FromStringHash($"OfflinePlayer:{username}"), username, this, world);

            //await this.SetCompression();
            await ConnectAsync();
        }
    }

    private async Task HandleEncryptionResponseAsync(byte[] data)
    {
        var encryptionResponse = EncryptionResponse.Deserialize(data);

        sharedKey = packetCryptography.Decrypt(encryptionResponse.SharedSecret);
        var decryptedToken = packetCryptography.Decrypt(encryptionResponse.VerifyToken);

        if (!decryptedToken.SequenceEqual(randomToken))
        {
            await DisconnectAsync("Invalid token...");
            return;
        }

        var serverId = sharedKey.Concat(packetCryptography.PublicKey).ToArray().MinecraftShaDigest();

        JoinedResponse? response = await MinecraftAPI.HasJoined(Player.Username, serverId);

        if (response is null)
        {
            Logger.LogWarning($"Failed to auth {Player.Username}");
            await DisconnectAsync("Unable to authenticate...");
            return;
        }

        EncryptionEnabled = true;
        minecraftStream = new AesStream(socketStream, sharedKey);

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

    private Task DeclareRecipesAsync() => QueuePacketAsync(UpdateRecipesPacket.FromRegistry);

    private async Task ConnectAsync()
    {
        await QueuePacketAsync(new LoginSuccess(Player.Uuid, Player.Username)
        {
            SkinProperties = this.cachedMojangUser?.Properties ?? new(),
        });

        Logger.LogDebug($"Sent Login success to user {Player.Username} {Player.Uuid}");

        State = ClientState.Play;

        await Player.LoadAsync();

        Server.OnlinePlayers.TryAdd(Player.Uuid, Player);

        if (!Registry.TryGetDimensionCodec(Player.World.DimensionName, out var codec) || !Registry.TryGetDimensionCodec("minecraft:overworld", out codec))
            throw new ApplicationException("Failed to retrieve proper dimension for player.");

        await QueuePacketAsync(new LoginPacket
        {
            EntityId = id,
            Gamemode = Player.Gamemode,
            DimensionNames = Registry.Dimensions.Values.Select(x => x.Name).ToList(),
            Codecs = new MixedCodec
            {
                Dimensions = Registry.Dimensions,
                Biomes = Registry.Biomes,
                ChatTypes = Registry.ChatTypes
            },
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
        await DeclareRecipesAsync();
        await QueuePacketAsync(new UpdateRecipeBookPacket
        {
            Action = UnlockRecipeAction.Init,
            FirstRecipeIds = Registry.Recipes.Keys.ToList(),
            SecondRecipeIds = Registry.Recipes.Keys.ToList()
        });

        await SendPlayerListDecoration();

        await SendPlayerInfoAsync();

        await Player.UpdateChunksAsync();

        await SendInfoAsync();

        await Server.Events.InvokePlayerJoinAsync(new PlayerJoinEventArgs(Player, DateTimeOffset.Now));
    }

    #region Packet sending
    internal async Task SendInfoAsync()
    {
        await QueuePacketAsync(new SetDefaultSpawnPositionPacket(Player.World.LevelData.SpawnPosition));

        Player.TeleportId = Globals.Random.Next(0, 999);

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
    }

    internal Task DisconnectAsync(ChatMessage reason)
    {
        return Task.Run(() => SendPacket(new DisconnectPacket(reason, State)));
    }

    internal Task SendTimeUpdateAsync()
    {
        return QueuePacketAsync(new UpdateTimePacket(Player.World.LevelData.Time, Player.World.LevelData.DayTime));
    }

    internal Task SendWeatherUpdateAsync()
    {
        return QueuePacketAsync(new GameEventPacket(Player.World.LevelData.Raining ? ChangeGameStateReason.BeginRaining : ChangeGameStateReason.EndRaining));
    }

    internal void ProcessKeepAlive(long id)
    {
        ping = (int)(DateTime.Now.Millisecond - id);
        SendPacket(new KeepAlivePacket(id));
        missedKeepalives++; // This will be decreased after an answer is received.

        if (missedKeepalives > config.MaxMissedKeepAlives)
        {
            // Too many keepalives missed, kill this connection.
            cancellationSource.Cancel();
        }

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

    internal Task SendCommandsAsync() => QueuePacketAsync(Registry.CommandsPacket);

    internal Task RemovePlayerFromListAsync(IPlayer player) => QueuePacketAsync(
        new PlayerInfoPacket(PlayerInfoAction.RemovePlayer,
        new InfoAction
        {
            Uuid = player.Uuid
        }));

    internal async Task AddPlayerToListAsync(IPlayer player)
    {
        var addAction = new AddPlayerInfoAction
        {
            Name = player.Username,
            Uuid = player.Uuid,
            Ping = Player.Ping,
            Gamemode = (int)Player.Gamemode,
            DisplayName = ChatMessage.Simple(player.Username)
        };

        if (config.OnlineMode)
        {
            var uuid = player.Uuid.ToString().Replace("-", "");
            addAction.Properties.AddRange(this.cachedMojangUser.Properties);
        }

        await QueuePacketAsync(new PlayerInfoPacket(PlayerInfoAction.AddPlayer, addAction));
    }

    internal async Task SendPlayerInfoAsync()
    {
        var infoActions = new List<InfoAction>();
        foreach (var (_, player) in Server.OnlinePlayers)
        {
            var addPlayerInforAction = new AddPlayerInfoAction()
            {
                Name = player.Username,
                Uuid = player.Uuid,
                Ping = player.Ping,
                Gamemode = (int)Player.Gamemode,
                DisplayName = ChatMessage.Simple(player.Username)
            };

            if (config.OnlineMode)
            {
                string uuid = player.Uuid.ToString().Replace("-", "");
                MojangUser? userWithSkin = await MinecraftAPI.GetUserAndSkinAsync(uuid);
                addPlayerInforAction.Properties.AddRange(userWithSkin.Properties);
            }

            infoActions.Add(addPlayerInforAction);
        }

        await QueuePacketAsync(new PlayerInfoPacket(PlayerInfoAction.AddPlayer, infoActions));
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
            Logger.LogError(e, $"Sending packet failed {packet.Id}");
        }
    }

    internal async Task QueuePacketAsync(IClientboundPacket packet)
    {
        var args = await Server.Events.InvokeQueuePacketAsync(new QueuePacketEventArgs(this, packet));
        if (args.Cancel)
        {
            Logger.LogDebug("A packet was set to queue but an event handler prevented it.");
        }
        else
        {
            await packetQueue.SendAsync(packet);
        }
    }

    internal Task SendChunkAsync(Chunk chunk)
    {
        return chunk is not null ? QueuePacketAsync(new ChunkDataPacket(chunk)) : Task.CompletedTask;
    }

    internal Task UnloadChunkAsync(int x, int z)
    {
        return LoadedChunks.Contains((x, z)) ? QueuePacketAsync(new UnloadChunkPacket(x, z)) : Task.CompletedTask;
    }

    private async Task SendServerBrand()
    {
        using var stream = new MinecraftStream();

        await stream.WriteStringAsync(Server.Brand);

        await QueuePacketAsync(new PluginMessagePacket("minecraft:brand", stream.ToArray()));
        Logger.LogDebug("Sent server brand.");
    }

    private async Task SendPlayerListDecoration()
    {
        ChatMessage? header = string.IsNullOrWhiteSpace(Server.ServerConfig.Header) ? null : ChatMessage.Simple(Server.ServerConfig.Header);
        ChatMessage? footer = string.IsNullOrWhiteSpace(Server.ServerConfig.Footer) ? null : ChatMessage.Simple(Server.ServerConfig.Footer);

        await QueuePacketAsync(new SetTabListHeaderAndFooterPacket(header, footer));
        Logger.LogDebug("Sent player list decoration");
    }
    #endregion Packet sending

    internal void Disconnect()
    {
        cancellationSource.Cancel();
        Disconnected?.Invoke(this);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;

        GC.SuppressFinalize(this);

        minecraftStream.Dispose();
        socket.Dispose();
        cancellationSource?.Dispose();
    }

    ~Client()
    {
        Dispose();
    }
}
