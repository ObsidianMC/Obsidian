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

public class Client : IDisposable
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
    public bool EncryptionEnabled { get; private set; }

    private const int CompressionThreshold = 256;

    internal TcpClient tcp;

    internal int ping;
    internal int missedKeepalives;
    internal int id;

    /// <summary>
    /// The client brand.
    /// </summary>
    public string Brand { get; set; }

    public ClientSettings ClientSettings { get; internal set; }

    public CancellationTokenSource Cancellation { get; private set; } = new();

    public ClientState State { get; private set; } = ClientState.Handshaking;

    public Server Server { get; private set; }
    public Player Player { get; private set; }

    public ILogger Logger => Server.Logger;

    public ConcurrentHashSet<(int X, int Z)> LoadedChunks { get; internal set; }

    public Client(TcpClient tcp, Config config, int playerId, Server originServer)
    {
        this.tcp = tcp;
        this.config = config;
        this.id = playerId;
        this.packetCryptography = new PacketCryptography();
        this.Server = originServer;
        this.LoadedChunks = new();
        this.handler = new ClientHandler(config);

        Stream parentStream = this.tcp.GetStream();
        this.minecraftStream = new MinecraftStream(parentStream);

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

        this.handler.RegisterHandlers();
    }

    private async Task<(int id, byte[] data)> GetNextPacketAsync()
    {
        int length = await this.minecraftStream.ReadVarIntAsync();
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
        while (!Cancellation.IsCancellationRequested && this.tcp.Connected)
        {
            (int id, byte[] data) = await this.GetNextPacketAsync();

            if (this.State == ClientState.Play && data.Length < 1)
                this.Disconnect();

            switch (this.State)
            {
                case ClientState.Status: // Server ping/list
                    switch (id)
                    {
                        case 0x00:
                            var status = new ServerStatus(Server);

                            await this.Server.Events.InvokeServerStatusRequest(new ServerStatusRequestEventArgs(this.Server, status));

                            this.SendPacket(new RequestResponse(status));
                            break;

                        case 0x01:
                            var pong = PingPong.Deserialize(data);

                            this.SendPacket(pong);

                            this.Disconnect();
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
                            this.Logger.LogDebug($"Client sent unexpected state ({ChatColor.Red}{(int)nextState}{ChatColor.White}), forcing it to disconnect");
                            await this.DisconnectAsync("you seem suspicious");
                        }

                        this.State = nextState;
                        this.Logger.LogInformation($"Handshaking with client (protocol: {ChatColor.Yellow}{handshake.Version.GetDescription()} {ChatColor.White}[{ChatColor.Yellow}{(int)handshake.Version}{ChatColor.White}], server: {ChatColor.Yellow}{handshake.ServerAddress}:{handshake.ServerPort}{ChatColor.White})");
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
                            this.Logger.LogError("Client in state Login tried to send an unimplemented packet. Forcing it to disconnect.");
                            await this.DisconnectAsync("Unknown Packet Id.");
                            break;

                        case 0x00:
                            var loginStart = LoginStart.Deserialize(data);

                            string username = config.MulitplayerDebugMode ? $"Player{Globals.Random.Next(1, 999)}" : loginStart.Username;


                            this.Logger.LogDebug($"Received login request from user {loginStart.Username}");

                            await this.Server.DisconnectIfConnectedAsync(username);

                            var world = this.Server.DefaultWorld as World;
                            if (this.config.OnlineMode)
                            {
                                var user = await MinecraftAPI.GetUserAsync(loginStart.Username);

                                if (config.WhitelistEnabled)
                                {
                                    var wlEntry = this.config.Whitelisted.FirstOrDefault(x => x.UUID == user.Id);

                                    if (wlEntry is null)
                                    {
                                        await this.DisconnectAsync("You are not whitelisted on this server\nContact server administrator");
                                        break;
                                    }

                                }

                                this.Player = new Player(Guid.Parse(user.Id), loginStart.Username, this, world);

                                this.packetCryptography.GenerateKeyPair();

                                var values = this.packetCryptography.GeneratePublicKeyAndToken();

                                this.randomToken = values.randomToken;

                                this.SendPacket(new EncryptionRequest(values.publicKey, this.randomToken));

                                break;
                            }

                            if (config.WhitelistEnabled && !config.Whitelisted.Any(x => x.Nickname == username))
                            {
                                await this.DisconnectAsync("You are not whitelisted on this server\nContact server administrator");
                                break;
                            }

                            this.Player = new Player(GuidHelper.FromStringHash($"OfflinePlayer:{username}"), username, this, world);

                            //await this.SetCompression();
                            await this.ConnectAsync();
                            break;
                        case 0x01:
                            var encryptionResponse = EncryptionResponse.Deserialize(data);

                            this.sharedKey = this.packetCryptography.Decrypt(encryptionResponse.SharedSecret);
                            var decryptedToken = this.packetCryptography.Decrypt(encryptionResponse.VerifyToken);

                            if (!decryptedToken.SequenceEqual(this.randomToken))
                            {
                                await this.DisconnectAsync("Invalid token..");
                                break;
                            }

                            var serverId = sharedKey.Concat(this.packetCryptography.PublicKey).ToArray().MinecraftShaDigest();

                            JoinedResponse response = await MinecraftAPI.HasJoined(this.Player.Username, serverId);

                            if (response is null)
                            {
                                this.Logger.LogWarning($"Failed to auth {this.Player.Username}");
                                await this.DisconnectAsync("Unable to authenticate..");
                                break;
                            }

                            this.EncryptionEnabled = true;
                            this.minecraftStream = new AesStream(this.tcp.GetStream(), this.sharedKey);

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

        if (this.State == ClientState.Play)
            await this.Server.Events.InvokePlayerLeaveAsync(new PlayerLeaveEventArgs(this.Player, DateTimeOffset.Now));

        if (tcp.Connected)
        {
            this.tcp.Close();

            if (this.Player != null)
                this.Server.OnlinePlayers.TryRemove(this.Player.Uuid, out var _);

            Disconnected?.Invoke(this);
        }
    }

    // TODO fix compression (.net 6)
    private void SetCompression()
    {
        this.SendPacket(new SetCompression(CompressionThreshold));
        this.compressionEnabled = true;
        this.Logger.LogDebug("Compression has been enabled.");
    }

    private Task DeclareRecipesAsync() => QueuePacketAsync(DeclareRecipes.FromRegistry);

    private async Task ConnectAsync()
    {
        await this.QueuePacketAsync(new LoginSuccess(this.Player.Uuid, this.Player.Username));
        this.Logger.LogDebug($"Sent Login success to user {this.Player.Username} {this.Player.Uuid}");

        this.State = ClientState.Play;

        await this.Player.LoadAsync();

        this.Server.OnlinePlayers.TryAdd(this.Player.Uuid, this.Player);

        if (!Registry.TryGetDimensionCodec(this.Player.World.DimensionName, out var codec) || !Registry.TryGetDimensionCodec("minecraft:overworld", out codec))
            throw new ApplicationException("Failed to retrieve proper dimension for player.");

        await this.QueuePacketAsync(new JoinGame
        {
            EntityId = this.id,

            Gamemode = this.Player.Gamemode,

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

        await this.SendServerBrand();
        await this.QueuePacketAsync(TagsPacket.FromRegistry);
        await this.SendCommandsAsync();
        await this.DeclareRecipesAsync();
        await this.QueuePacketAsync(new UnlockRecipes
        {
            Action = UnlockRecipeAction.Init,
            FirstRecipeIds = Registry.Recipes.Keys.ToList(),
            SecondRecipeIds = Registry.Recipes.Keys.ToList()
        });

        await this.SendPlayerListDecoration();
        await this.SendPlayerInfoAsync();

        await this.UpdateChunksAsync();

        await this.SendInfoAsync();

        await this.Server.Events.InvokePlayerJoinAsync(new PlayerJoinEventArgs(this.Player, DateTimeOffset.Now));
    }

    #region Packet sending
    internal async Task SendInfoAsync()
    {
        await this.QueuePacketAsync(new SpawnPosition(Player.World.LevelData.SpawnPosition));

        this.Player.TeleportId = Globals.Random.Next(0, 999);

        await this.QueuePacketAsync(new PlayerPositionAndLook
        {
            Position = this.Player.Position,
            Yaw = 0,
            Pitch = 0,
            Flags = PositionFlags.None,
            TeleportId = this.Player.TeleportId
        });

        await this.SendTimeUpdateAsync();
        await this.SendWeatherUpdateAsync();

        await this.QueuePacketAsync(new WindowItems(0, this.Player.Inventory.ToList())
        {
            StateId = this.Player.Inventory.StateId++,
            CarriedItem = this.Player.GetHeldItem(),
        });
    }

    internal Task DisconnectAsync(ChatMessage reason) => Task.Run(() => SendPacket(new Disconnect(reason, this.State)));

    internal Task SendTimeUpdateAsync() => this.QueuePacketAsync(new TimeUpdate(this.Player.World.LevelData.Time, this.Player.World.LevelData.DayTime));
    internal Task SendWeatherUpdateAsync() =>
        this.QueuePacketAsync(new ChangeGameState(this.Player.World.LevelData.Raining ? ChangeGameStateReason.BeginRaining : ChangeGameStateReason.EndRaining));

    internal void ProcessKeepAlive(long id)
    {
        this.ping = (int)(DateTime.Now.Millisecond - id);
        this.SendPacket(new KeepAlivePacket(id));
        this.missedKeepalives++; // This will be decreased after an answer is received.

        if (this.missedKeepalives > this.config.MaxMissedKeepAlives)
        {
            // Too many keepalives missed, kill this connection.
            this.Cancellation.Cancel();
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

    internal Task SendCommandsAsync() => this.QueuePacketAsync(Registry.DeclareCommandsPacket);

    internal Task RemovePlayerFromListAsync(IPlayer player) => this.QueuePacketAsync(new PlayerInfoPacket(PlayerInfoAction.RemovePlayer, new InfoAction
    {
        Uuid = player.Uuid
    }));

    internal async Task AddPlayerToListAsync(IPlayer player)
    {
        var addAction = new AddPlayerInfoAction
        {
            Name = player.Username,
            Uuid = player.Uuid,
            Ping = this.Player.Ping,
            Gamemode = (int)this.Player.Gamemode,
            DisplayName = ChatMessage.Simple(player.Username)
        };

        if (this.config.OnlineMode)
        {
            var uuid = player.Uuid.ToString().Replace("-", "");
            var skin = await MinecraftAPI.GetUserAndSkinAsync(uuid);
            addAction.Properties.AddRange(skin.Properties);
        }

        await this.QueuePacketAsync(new PlayerInfoPacket(PlayerInfoAction.AddPlayer, addAction));
    }
    internal async Task SendPlayerInfoAsync()
    {
        var list = new List<InfoAction>();

        foreach (var (_, player) in this.Server.OnlinePlayers)
        {
            var piaa = new AddPlayerInfoAction()
            {
                Name = player.Username,
                Uuid = player.Uuid,
                Ping = player.Ping,
                Gamemode = (int)Player.Gamemode,
                DisplayName = ChatMessage.Simple(player.Username)
            };

            if (this.config.OnlineMode)
            {
                var uuid = player.Uuid.ToString().Replace("-", "");
                var skin = await MinecraftAPI.GetUserAndSkinAsync(uuid);
                piaa.Properties.AddRange(skin.Properties);
            }

            list.Add(piaa);
        }

        await this.QueuePacketAsync(new PlayerInfoPacket(PlayerInfoAction.AddPlayer, list));
    }

    internal void SendPacket(IClientboundPacket packet)
    {
        try
        {
            if (!this.compressionEnabled)
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
        var args = await this.Server.Events.InvokeQueuePacketAsync(new QueuePacketEventArgs(this, packet));

        if (args.Cancel)
        {
            this.Logger.LogDebug("A packet was set to queue but an event handler prevented it.");
            return;
        }

        await this.packetQueue.SendAsync(packet);
        //this.Logger.LogDebug($"Queuing packet: {packet} (0x{packet.Id:X2})");
    }

    internal async Task UpdateChunksAsync(bool unloadAll = false)
    {
        if (unloadAll)
        {
            if (!this.Player.Respawning)
            {
                foreach (var (X, Z) in this.LoadedChunks)
                    await this.UnloadChunkAsync(X, Z);
            }

            this.LoadedChunks.Clear();
        }

        List<(int X, int Z)> clientNeededChunks = new();
        List<(int X, int Z)> clientUnneededChunks = new(this.LoadedChunks);

        (int playerChunkX, int playerChunkZ) = this.Player.Position.ToChunkCoord();
        (int lastPlayerChunkX, int lastPlayerChunkZ) = this.Player.LastPosition.ToChunkCoord();

        int dist = (this.ClientSettings?.ViewDistance ?? 14) - 2;
        for (int x = playerChunkX + dist; x > playerChunkX - dist; x--)
            for (int z = playerChunkZ + dist; z > playerChunkZ - dist; z--)
                clientNeededChunks.Add((x, z));

        clientUnneededChunks = clientUnneededChunks.Except(clientNeededChunks).ToList();
        clientNeededChunks = clientNeededChunks.Except(this.LoadedChunks).ToList();
        clientNeededChunks.Sort((chunk1, chunk2) =>
        {
            return Math.Abs(playerChunkX - chunk1.X) +
            Math.Abs(playerChunkZ - chunk1.Z) <
            Math.Abs(playerChunkX - chunk2.X) +
            Math.Abs(playerChunkZ - chunk2.Z) ? -1 : 1;
        });

        await Parallel.ForEachAsync(clientUnneededChunks, async (chunkLoc, _) =>
        {
            await this.UnloadChunkAsync(chunkLoc.X, chunkLoc.Z);
            this.LoadedChunks.TryRemove(chunkLoc);
        });

        await Parallel.ForEachAsync(clientNeededChunks, async (chunkLoc, _) =>
        {
            var chunk = await this.Player.World.GetChunkAsync(chunkLoc.X, chunkLoc.Z);
            if (chunk is not null)
            {
                await this.SendChunkAsync(chunk);
                this.LoadedChunks.Add((chunk.X, chunk.Z));
            }
        });
    }


    internal Task SendChunkAsync(Chunk chunk) => chunk != null ? this.QueuePacketAsync(new ChunkDataPacket(chunk)) : Task.CompletedTask;

    public Task UnloadChunkAsync(int x, int z) => this.LoadedChunks.Contains((x, z)) ? this.QueuePacketAsync(new UnloadChunk(x, z)) : Task.CompletedTask;

    private async Task SendServerBrand()
    {
        using var stream = new MinecraftStream();

        await stream.WriteStringAsync("obsidian");

        await this.QueuePacketAsync(new PluginMessage("minecraft:brand", stream.ToArray()));
        this.Logger.LogDebug("Sent server brand.");
    }

    private async Task SendPlayerListDecoration()
    {
        var header = string.IsNullOrWhiteSpace(Server.Config.Header) ? null : ChatMessage.Simple(Server.Config.Header);
        var footer = string.IsNullOrWhiteSpace(Server.Config.Footer) ? null : ChatMessage.Simple(Server.Config.Footer);

        await this.QueuePacketAsync(new PlayerListHeaderFooter(header, footer));
        this.Logger.LogDebug("Sent player list decoration");
    }
    #endregion Packet sending

    internal void Disconnect()
    {
        Cancellation.Cancel();
        Disconnected?.Invoke(this);
    }

    #region Disposing
    protected virtual void Dispose(bool disposing)
    {
        if (this.disposed)
            return;

        if (disposing)
        {
            this.minecraftStream.Dispose();
            this.tcp.Dispose();

            if (this.Cancellation != null)
                this.Cancellation.Dispose();
        }

        this.Player = null;
        this.minecraftStream = null;
        this.tcp = null;
        this.Cancellation = null;

        this.randomToken = null;
        this.sharedKey = null;
        this.Player = null;
        this.ClientSettings = null;
        this.config = null;
        this.Server = null;

        this.disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);

        GC.SuppressFinalize(this);
    }

    ~Client()
    {
        this.Dispose(false);
    }
    #endregion
}
