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
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks.Dataflow;

namespace Obsidian;

public sealed class Client : IDisposable
{
    public event Action<Client>? Disconnected;

    private byte[] randomToken;
    private byte[] sharedKey;

    private readonly BufferBlock<IClientboundPacket> packetQueue;

    private readonly PacketCryptography packetCryptography;

    private readonly ClientHandler handler;

    private MinecraftStream minecraftStream;

    private Config config;

    private bool disposed;
    private bool compressionEnabled;
    private bool EncryptionEnabled { get; set; }

    private const int CompressionThreshold = 256;

    private TcpClient tcp;

    internal int ping;
    internal int missedKeepalives;
    internal readonly int id;

    /// <summary>
    /// The client brand.
    /// </summary>
    public string Brand { get; set; }

    public ClientSettings ClientSettings { get; internal set; }

    private CancellationTokenSource Cancellation { get; set; } = new();

    public ClientState State { get; private set; } = ClientState.Handshaking;

    public Server Server { get; private set; }
    public Player Player { get; private set; }

    private ILogger Logger => Server.Logger;

    public ConcurrentHashSet<(int X, int Z)> LoadedChunks { get; internal set; }

    public Client(TcpClient tcp, Config config, int playerId, Server originServer)
    {
        this.tcp = tcp;
        this.config = config;
        id = playerId;
        packetCryptography = new PacketCryptography();
        Server = originServer;
        LoadedChunks = new();
        handler = new ClientHandler(config);

        Stream parentStream = this.tcp.GetStream();
        minecraftStream = new MinecraftStream(parentStream);

        var blockOptions = new ExecutionDataflowBlockOptions { CancellationToken = Cancellation.Token, EnsureOrdered = true };
        packetQueue = new BufferBlock<IClientboundPacket>(blockOptions);
        var sendPacketBlock = new ActionBlock<IClientboundPacket>(packet =>
        {
            if (tcp.Connected)
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

        await using (var packetStream = new MinecraftStream(receivedData))
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
        while (!Cancellation.IsCancellationRequested && tcp.Connected)
        {
            (int id, byte[] data) = await GetNextPacketAsync();

            if (State == ClientState.Play && data.Length < 1)
                Disconnect();

            switch (State)
            {
                case ClientState.Status: // Server ping/list
                    switch (id)
                    {
                        case 0x00:
                            var status = new ServerStatus(Server);

                            await Server.Events.InvokeServerStatusRequest(new ServerStatusRequestEventArgs(Server, status));

                            SendPacket(new RequestResponse(status));
                            break;

                        case 0x01:
                            var pong = PingPong.Deserialize(data);

                            SendPacket(pong);

                            Disconnect();
                            break;
                    }
                    break;

                case ClientState.Handshaking:
                    if (id == 0x00)
                    {
                        var handshake = Handshake.Deserialize(data);

                        var nextState = handshake.NextState;

                        if (nextState != ClientState.Status && nextState != ClientState.Login)
                        {
                            Logger.LogDebug($"Client sent unexpected state ({ChatColor.Red}{(int)nextState}{ChatColor.White}), forcing it to disconnect");
                            await DisconnectAsync("you seem suspicious");
                        }

                        State = nextState;
                        Logger.LogInformation($"Handshaking with client (protocol: {ChatColor.Yellow}{handshake.Version.GetDescription()} {ChatColor.White}[{ChatColor.Yellow}{(int)handshake.Version}{ChatColor.White}], server: {ChatColor.Yellow}{handshake.ServerAddress}:{handshake.ServerPort}{ChatColor.White})");
                    }
                    else
                    {
                        // Handle legacy ping
                    }
                    break;

                case ClientState.Login:
                    switch (id)
                    {
                        default:
                            Logger.LogError("Client in state Login tried to send an unimplemented packet. Forcing it to disconnect.");
                            await DisconnectAsync("Unknown Packet Id.");
                            break;

                        case 0x00:
                            var loginStart = LoginStart.Deserialize(data);

                            string username = config.MulitplayerDebugMode ? $"Player{Globals.Random.Next(1, 999)}" : loginStart.Username;


                            Logger.LogDebug($"Received login request from user {loginStart.Username}");

                            await Server.DisconnectIfConnectedAsync(username);

                            var world = Server.DefaultWorld as World;
                            if (config.OnlineMode)
                            {
                                var user = await MinecraftAPI.GetUserAsync(loginStart.Username);

                                if (config.WhitelistEnabled)
                                {
                                    var wlEntry = config.Whitelisted.FirstOrDefault(x => x.UUID == user.Id);

                                    if (wlEntry is null)
                                    {
                                        await DisconnectAsync("You are not whitelisted on this server\nContact server administrator");
                                        break;
                                    }

                                }

                                Player = new Player(Guid.Parse(user.Id), loginStart.Username, this, world);

                                packetCryptography.GenerateKeyPair();

                                var values = packetCryptography.GeneratePublicKeyAndToken();

                                randomToken = values.randomToken;

                                SendPacket(new EncryptionRequest(values.publicKey, randomToken));

                                break;
                            }

                            if (config.WhitelistEnabled && config.Whitelisted.All(x => x.Nickname != username))
                            {
                                await DisconnectAsync("You are not whitelisted on this server\nContact server administrator");
                                break;
                            }

                            Player = new Player(GuidHelper.FromStringHash($"OfflinePlayer:{username}"), username, this, world);

                            //await this.SetCompression();
                            await ConnectAsync();
                            break;
                        case 0x01:
                            var encryptionResponse = EncryptionResponse.Deserialize(data);

                            sharedKey = packetCryptography.Decrypt(encryptionResponse.SharedSecret);
                            var decryptedToken = packetCryptography.Decrypt(encryptionResponse.VerifyToken);

                            if (!decryptedToken.SequenceEqual(randomToken))
                            {
                                await DisconnectAsync("Invalid token..");
                                break;
                            }

                            var serverId = sharedKey.Concat(packetCryptography.PublicKey).ToArray().MinecraftShaDigest();

                            JoinedResponse response = await MinecraftAPI.HasJoined(Player.Username, serverId);

                            if (response is null)
                            {
                                Logger.LogWarning($"Failed to auth {Player.Username}");
                                await DisconnectAsync("Unable to authenticate..");
                                break;
                            }

                            EncryptionEnabled = true;
                            minecraftStream = new AesStream(tcp.GetStream(), sharedKey);

                            //await this.SetCompression();
                            await ConnectAsync();
                            break;
                        case 0x02:
                            // Login Plugin Response
                            break;
                    }
                    break;

                case ClientState.Play:
                    var packetReceivedEventArgs = new PacketReceivedEventArgs(Player, id, data);
                    await Server.Events.InvokePacketReceivedAsync(packetReceivedEventArgs);

                    if (!packetReceivedEventArgs.Cancel)
                    {
                        await handler.HandlePlayPackets(id, data, this);
                    }
                    break;
            }

            //await Task.Delay(50);
        }

        Logger.LogInformation($"Disconnected client");

        if (State == ClientState.Play)
            await Server.Events.InvokePlayerLeaveAsync(new PlayerLeaveEventArgs(Player, DateTimeOffset.Now));

        if (tcp.Connected)
        {
            tcp.Close();

            if (Player != null)
                Server.OnlinePlayers.TryRemove(Player.Uuid, out var _);

            Disconnected?.Invoke(this);
        }
    }

    // TODO fix compression (.net 6)
    private void SetCompression()
    {
        SendPacket(new SetCompression(CompressionThreshold));
        compressionEnabled = true;
        Logger.LogDebug("Compression has been enabled.");
    }

    private Task DeclareRecipesAsync() => QueuePacketAsync(DeclareRecipes.FromRegistry);

    private async Task ConnectAsync()
    {
        await QueuePacketAsync(new LoginSuccess(Player.Uuid, Player.Username));
        Logger.LogDebug($"Sent Login success to user {Player.Username} {Player.Uuid}");

        State = ClientState.Play;

        await Player.LoadAsync();

        Server.OnlinePlayers.TryAdd(Player.Uuid, Player);

        if (!Registry.TryGetDimensionCodec(Player.World.DimensionName, out var codec) || !Registry.TryGetDimensionCodec("minecraft:overworld", out codec))
            throw new ApplicationException("Failed to retrieve proper dimension for player.");

        await QueuePacketAsync(new JoinGame
        {
            EntityId = id,

            Gamemode = Player.Gamemode,

            WorldNames = new List<string> { "minecraft:world" },

            Codecs = new MixedCodec
            {
                Dimensions = Registry.Dimensions,
                Biomes = Registry.Biomes
            },

            Dimension = codec,

            DimensionName = codec.Name,

            HashedSeed = 0,

            ReducedDebugInfo = false,

            EnableRespawnScreen = true,

            Flat = false
        });

        await SendServerBrand();
        await QueuePacketAsync(TagsPacket.FromRegistry);
        await SendCommandsAsync();
        await DeclareRecipesAsync();
        await QueuePacketAsync(new UnlockRecipes
        {
            Action = UnlockRecipeAction.Init,
            FirstRecipeIds = Registry.Recipes.Keys.ToList(),
            SecondRecipeIds = Registry.Recipes.Keys.ToList()
        });

        await SendPlayerListDecoration();
        await SendPlayerInfoAsync();

        await UpdateChunksAsync();

        await SendInfoAsync();

        await Server.Events.InvokePlayerJoinAsync(new PlayerJoinEventArgs(Player, DateTimeOffset.Now));
    }

    #region Packet sending
    internal async Task SendInfoAsync()
    {
        await QueuePacketAsync(new SpawnPosition(Player.World.LevelData.SpawnPosition));

        Player.TeleportId = Globals.Random.Next(0, 999);

        await QueuePacketAsync(new PlayerPositionAndLook
        {
            Position = Player.Position,
            Yaw = 0,
            Pitch = 0,
            Flags = PositionFlags.None,
            TeleportId = Player.TeleportId
        });

        await SendTimeUpdateAsync();
        await SendWeatherUpdateAsync();

        await QueuePacketAsync(new WindowItems(0, Player.Inventory.ToList())
        {
            StateId = Player.Inventory.StateId++,
            CarriedItem = Player.GetHeldItem(),
        });
    }

    internal Task DisconnectAsync(ChatMessage reason) => Task.Run(() => SendPacket(new Disconnect(reason, State)));

    private Task SendTimeUpdateAsync() => QueuePacketAsync(new TimeUpdate(Player.World.LevelData.Time, Player.World.LevelData.DayTime));

    private Task SendWeatherUpdateAsync() =>
        QueuePacketAsync(new ChangeGameState(Player.World.LevelData.Raining
            ? ChangeGameStateReason.BeginRaining
            : ChangeGameStateReason.EndRaining));

    internal void ProcessKeepAlive(long id)
    {
        ping = (int)(DateTime.Now.Millisecond - id);
        SendPacket(new KeepAlivePacket(id));
        missedKeepalives++; // This will be decreased after an answer is received.

        if (missedKeepalives > config.MaxMissedKeepAlives)
        {
            // Too many keepalives missed, kill this connection.
            Cancellation.Cancel();
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

    internal Task SendCommandsAsync() => QueuePacketAsync(Registry.DeclareCommandsPacket);

    internal Task RemovePlayerFromListAsync(IPlayer player) => QueuePacketAsync(new PlayerInfoPacket(PlayerInfoAction.RemovePlayer, new InfoAction
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
            var skin = await MinecraftAPI.GetUserAndSkinAsync(uuid);
            addAction.Properties.AddRange(skin.Properties);
        }

        await QueuePacketAsync(new PlayerInfoPacket(PlayerInfoAction.AddPlayer, addAction));
    }

    private async Task SendPlayerInfoAsync()
    {
        var list = new List<InfoAction>();

        foreach (var (_, player) in Server.OnlinePlayers)
        {
            var piaa = new AddPlayerInfoAction()
            {
                Name = player.Username,
                Uuid = player.Uuid,
                Ping = player.Ping,
                Gamemode = (int)Player.Gamemode,
                DisplayName = ChatMessage.Simple(player.Username)
            };

            if (config.OnlineMode)
            {
                var uuid = player.Uuid.ToString().Replace("-", "");
                var skin = await MinecraftAPI.GetUserAndSkinAsync(uuid);
                piaa.Properties.AddRange(skin.Properties);
            }

            list.Add(piaa);
        }

        await QueuePacketAsync(new PlayerInfoPacket(PlayerInfoAction.AddPlayer, list));
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
            if (!tcp.Connected)
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
            return;
        }

        await packetQueue.SendAsync(packet);
        //this.Logger.LogDebug($"Queuing packet: {packet} (0x{packet.Id:X2})");
    }

    internal async Task UpdateChunksAsync(bool unloadAll = false)
    {
        if (unloadAll)
        {
            if (!Player.Respawning)
            {
                foreach (var (X, Z) in LoadedChunks)
                    await UnloadChunkAsync(X, Z);
            }

            LoadedChunks.Clear();
        }

        List<(int X, int Z)> clientNeededChunks = new();
        List<(int X, int Z)> clientUnneededChunks = new(LoadedChunks);

        (int playerChunkX, int playerChunkZ) = Player.Position.ToChunkCoord();
        (int lastPlayerChunkX, int lastPlayerChunkZ) = Player.LastPosition.ToChunkCoord();

        int dist = (ClientSettings?.ViewDistance ?? 14) - 2;
        for (int x = playerChunkX + dist; x > playerChunkX - dist; x--)
            for (int z = playerChunkZ + dist; z > playerChunkZ - dist; z--)
                clientNeededChunks.Add((x, z));

        clientUnneededChunks = clientUnneededChunks.Except(clientNeededChunks).ToList();
        clientNeededChunks = clientNeededChunks.Except(LoadedChunks).ToList();
        clientNeededChunks.Sort((chunk1, chunk2) => Math.Abs(playerChunkX - chunk1.X) +
                                                    Math.Abs(playerChunkZ - chunk1.Z) <
                                                    Math.Abs(playerChunkX - chunk2.X) +
                                                    Math.Abs(playerChunkZ - chunk2.Z) ? -1 : 1);

        await Parallel.ForEachAsync(clientUnneededChunks, async (chunkLoc, _) =>
        {
            await UnloadChunkAsync(chunkLoc.X, chunkLoc.Z);
            LoadedChunks.TryRemove(chunkLoc);
        });

        await Parallel.ForEachAsync(clientNeededChunks, async (chunkLoc, _) =>
        {
            var chunk = await Player.World.GetChunkAsync(chunkLoc.X, chunkLoc.Z);
            if (chunk is not null)
            {
                await SendChunkAsync(chunk);
                LoadedChunks.Add((chunk.X, chunk.Z));
            }
        });
    }


    internal Task SendChunkAsync(Chunk chunk) => chunk != null ? QueuePacketAsync(new ChunkDataPacket(chunk)) : Task.CompletedTask;

    public Task UnloadChunkAsync(int x, int z) => LoadedChunks.Contains((x, z)) ? QueuePacketAsync(new UnloadChunk(x, z)) : Task.CompletedTask;

    private async Task SendServerBrand()
    {
        using var stream = new MinecraftStream();

        await stream.WriteStringAsync("obsidian");

        await QueuePacketAsync(new PluginMessage("minecraft:brand", stream.ToArray()));
        Logger.LogDebug("Sent server brand.");
    }

    private async Task SendPlayerListDecoration()
    {
        var header = string.IsNullOrWhiteSpace(Server.Config.Header) ? null : ChatMessage.Simple(Server.Config.Header);
        var footer = string.IsNullOrWhiteSpace(Server.Config.Footer) ? null : ChatMessage.Simple(Server.Config.Footer);

        await QueuePacketAsync(new PlayerListHeaderFooter(header, footer));
        Logger.LogDebug("Sent player list decoration");
    }
    #endregion Packet sending

    internal void Disconnect()
    {
        Cancellation.Cancel();
        Disconnected?.Invoke(this);
    }

    #region Disposing

    private void Dispose(bool disposing)
    {
        if (disposed)
            return;

        if (disposing)
        {
            minecraftStream.Dispose();
            tcp.Dispose();

            Cancellation.Dispose();
        }

        Player = null;
        minecraftStream = null;
        tcp = null;
        Cancellation = null;

        randomToken = null;
        sharedKey = null;
        Player = null;
        ClientSettings = null;
        config = null;
        Server = null;

        disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);

        GC.SuppressFinalize(this);
    }

    ~Client()
    {
        Dispose(false);
    }
    #endregion
}
