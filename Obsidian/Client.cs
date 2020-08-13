using DaanV2.UUID;
using Obsidian.Chat;
using Obsidian.ChunkData;
using Obsidian.Commands;
using Obsidian.Entities;
using Obsidian.Events.EventArgs;
using Obsidian.Logging;
using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Handshaking;
using Obsidian.Net.Packets.Login;
using Obsidian.Net.Packets.Play;
using Obsidian.Net.Packets.Status;
using Obsidian.PlayerData;
using Obsidian.PlayerData.Info;
using Obsidian.Serializer;
using Obsidian.Util;
using Obsidian.Util.DataTypes;
using Obsidian.Util.Debug;
using Obsidian.Util.Mojang;
using Obsidian.World;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Obsidian
{
    public class Client : IDisposable
    {
        private byte[] randomToken;
        private byte[] sharedKey;

        private PacketCryptography packetCryptography;

        private MinecraftStream minecraftStream;
        private PacketDebugStream debugStream;

        private Config config;

        private bool disposed;
        private bool compressionEnabled;
        private bool encryptionEnabled;

        private const int compressionThreshold = 256;

        internal TcpClient tcp;

        internal int ping;
        internal int missedKeepalives;
        internal int id;

        public ClientSettings ClientSettings { get; internal set; }

        public CancellationTokenSource Cancellation { get; private set; } = new CancellationTokenSource();

        public ClientState State { get; private set; } = ClientState.Handshaking;

        public ConcurrentQueue<Packet> PacketQueue { get; } = new ConcurrentQueue<Packet>();

        public Server Server { get; private set; }
        public Player Player { get; private set; }

        public AsyncLogger Logger => this.Server.Logger;

        public Client(TcpClient tcp, Config config, int playerId, Server originServer)
        {
            this.tcp = tcp;
            this.config = config;
            this.id = playerId;
            this.packetCryptography = new PacketCryptography();
            this.Server = originServer;

            Stream parentStream = this.tcp.GetStream();
#if DEBUG
            //parentStream = this.DebugStream = new PacketDebugStream(parentStream);
#endif
            this.minecraftStream = new MinecraftStream(parentStream);
        }

        ~Client()
        {
            Dispose(false);
        }

        #region Packet Sending Methods

        internal Task DisconnectAsync(ChatMessage reason) => this.SendPacket(new Disconnect(reason, this.State));

        internal async Task ProcessKeepAlive(long id)
        {
            this.ping = (int)(DateTime.Now.Millisecond - id);
            await this.SendPacket(new KeepAlive(id));
            missedKeepalives += 1; // This will be decreased after an answer is received.
            if (missedKeepalives > this.config.MaxMissedKeepalives)
            {
                // Too many keepalives missed, kill this connection.
                Cancellation.Cancel();
            }

            /////Sending ping change in background
            ///await Task.Run(async delegate ()
            ///{
            ///    foreach (Client client in OriginServer.Clients.Where(c => c.IsPlaying))
            ///    {
            ///        await PacketHandler.CreateAsync(new PlayerInfo(2, new List<PlayerInfoAction>()
            ///        {
            ///            new PlayerInfoUpdatePingAction()
            ///            {
            ///                Ping = this.Ping
            ///            }
            ///        }), this.MinecraftStream);
            ///    }
            ///}).ConfigureAwait(false);
        }

        internal async Task SendPlayerLookPositionAsync(Transform poslook, PositionFlags posflags, int tpid = 0)
        {
            await this.SendPacket(new PlayerPositionLook(poslook, posflags, tpid));
        }

        internal async Task SendBlockChangeAsync(BlockChange b)
        {
            await this.Logger.LogMessageAsync($"Sending block change to {Player.Username}");
            await this.SendPacket(b);
            await this.Logger.LogMessageAsync($"Block change sent to {Player.Username}");
        }

        internal async Task SendSpawnMobAsync(int id, Guid uuid, int type, Transform transform, byte headPitch, Velocity velocity, Entity entity)
        {
            await this.SendPacket(new SpawnMob(id, uuid, type, transform, headPitch, velocity, entity));

            await this.Logger.LogDebugAsync($"Spawned entity with id {id} for player {this.Player.Username}");
        }

        internal async Task SendEntity(EntityPacket packet)
        {
            await SendPacket(packet);
            await this.Logger.LogDebugAsync($"Sent entity with id {packet.Id} for player {this.Player.Username}");
        }

        internal async Task SendDeclareCommandsAsync()
        {
            var packet = new DeclareCommands();

            var node = new CommandNode()
            {
                Type = CommandNodeType.Root
            };
            foreach (Qmmands.Command command in this.Server.Commands.GetAllCommands())
            {
                var commandNode = new CommandNode()
                {
                    Name = command.Name,
                    Type = CommandNodeType.Literal
                };

                foreach (Qmmands.Parameter parameter in command.Parameters)
                {
                    var parameterNode = new CommandNode()
                    {
                        Name = parameter.Name,
                        Type = CommandNodeType.Argument,
                    };

                    Type type = parameter.Type;

                    if (type == typeof(string)) parameterNode.Parser = new StringCommandParser(parameter.IsRemainder ? StringType.GreedyPhrase : StringType.QuotablePhrase);
                    else if (type == typeof(double)) parameterNode.Parser = new EmptyFlagsCommandParser("brigadier:double");
                    else if (type == typeof(float)) parameterNode.Parser = new EmptyFlagsCommandParser("brigadier:float");
                    else if (type == typeof(int)) parameterNode.Parser = new EmptyFlagsCommandParser("brigadier:integer");
                    else if (type == typeof(bool)) parameterNode.Parser = new CommandParser("brigadier:bool");
                    else continue;

                    commandNode.Children.Add(parameterNode);
                }

                if (commandNode.Children.Count > 0)
                {
                    commandNode.Children[0].Type |= CommandNodeType.IsExecutabe;
                }
                else
                {
                    commandNode.Type |= CommandNodeType.IsExecutabe;
                }

                node.Children.Add(commandNode);

                packet.AddNode(node);
            }

            await this.QueuePacketAsync(packet);
            await this.Logger.LogDebugAsync("Sent Declare Commands packet.");
        }

        internal async Task RemovePlayerFromListAsync(Player player)
        {
            var list = new List<PlayerInfoAction>
            {
                new PlayerInfoAction
                {
                    Uuid = player.Uuid
                }
            };

            await SendPacket(new PlayerInfo(4, list));
            await this.Logger.LogDebugAsync($"Removed Player to player info list from {this.Player.Username}");
        }

        internal async Task AddPlayerToListAsync(Player player)
        {
            var list = new List<PlayerInfoAction>
            {
                new PlayerInfoAddAction
                {
                    Name = player.Username,
                    Uuid = player.Uuid,
                    Ping = this.Player.Ping,
                    Gamemode = (int)this.Player.Gamemode,
                    DisplayName = ChatMessage.Simple(player.Username)
                }
            };

            await SendPacket(new PlayerInfo(0, list));
            await this.Logger.LogDebugAsync($"Added Player to player info list from {this.Player.Username}");
        }

        internal async Task SendPlayerInfoAsync()
        {
            var list = new List<PlayerInfoAction>();

            foreach (Player player in this.Server.OnlinePlayers.Values)
            {
                var piaa = new PlayerInfoAddAction()
                {
                    Name = player.Username,
                    Uuid = player.Uuid,
                    Ping = player.Ping,
                    Gamemode = (int)Player.Gamemode,
                    DisplayName = ChatMessage.Simple(player.Username)
                };

                if (this.config.OnlineMode)
                {
                    var uuid = player.Uuid.Replace("-", "");
                    var skin = await MinecraftAPI.GetUserAndSkinAsync(uuid);
                    piaa.Properties.AddRange(skin.Properties);
                }

                list.Add(piaa);
            }

            await SendPacket(new PlayerInfo(0, list));
            await this.Logger.LogDebugAsync($"Sent Player Info packet from {this.Player.Username}");
        }

        internal async Task SendPlayerListHeaderFooterAsync(ChatMessage header, ChatMessage footer)
        {
            await SendPacket(new PlayerListHeaderFooter(header, footer));
            await this.Logger.LogDebugAsync("Sent Player List Footer Header packet.");
        }

        #endregion Packet Sending Methods

        private async Task<Packet> GetNextPacketAsync()
        {
            if (this.compressionEnabled)
            {
                return await PacketHandler.ReadCompressedPacketAsync(this.minecraftStream);
            }
            else
            {
                return await PacketHandler.ReadPacketAsync(this.minecraftStream);
            }
        }

        public async Task StartConnectionAsync()
        {
            _ = Task.Run(ProcessQueue);

            while (!Cancellation.IsCancellationRequested && this.tcp.Connected)
            {
                Packet packet = await this.GetNextPacketAsync();

                if (this.State == ClientState.Play && packet.data.Length < 1)
                    this.Disconnect();

                switch (this.State)
                {
                    case ClientState.Status: //server ping/list
                        switch (packet.id)
                        {
                            case 0x00:
                                var status = new ServerStatus(Server);
                                await this.SendPacket(new RequestResponse(status));
                                break;

                            case 0x01:
                                await this.SendPacket(new PingPong(packet.data));
                                this.Disconnect();
                                break;
                        }
                        break;

                    case ClientState.Handshaking:
                        if (packet.id == 0x00)
                        {
                            if (packet == null)
                                throw new InvalidOperationException();

                            var handshake = await PacketSerializer.DeserializeAsync<Handshake>(packet.data);

                            var nextState = handshake.NextState;

                            if (nextState != ClientState.Status && nextState != ClientState.Login)
                            {
                                await this.Logger.LogDebugAsync($"Client sent unexpected state ({(int)nextState}), forcing it to disconnect");
                                await this.DisconnectAsync(ChatMessage.Simple("you seem suspicious"));
                            }

                            this.State = nextState;
                            await this.Logger.LogMessageAsync($"Handshaking with client (protocol: {handshake.Version}, server: {handshake.ServerAddress}:{handshake.ServerPort})");
                        }
                        else
                        {
                            //Handle legacy ping stuff
                        }
                        break;

                    case ClientState.Login:
                        switch (packet.id)
                        {
                            default:
                                await this.Logger.LogErrorAsync("Client in state Login tried to send an unimplemented packet. Forcing it to disconnect.");
                                await this.DisconnectAsync(ChatMessage.Simple("Unknown Packet Id."));
                                break;

                            case 0x00:
                                var loginStart = await PacketSerializer.DeserializeAsync<LoginStart>(packet.data);

                                string username = config.MulitplayerDebugMode ? $"Player{Program.Random.Next(1, 999)}" : loginStart.Username;

                                await this.Logger.LogDebugAsync($"Received login request from user {loginStart.Username}");

                                await this.Server.DisconnectIfConnectedAsync(username);

                                if (this.config.OnlineMode)
                                {
                                    var user = await MinecraftAPI.GetUserAsync(loginStart.Username);

                                    var uuid = Guid.Parse(user.Id).ToString();
                                    this.Player = new Player(uuid, loginStart.Username, this);

                                    this.packetCryptography.GenerateKeyPair();

                                    var values = this.packetCryptography.GeneratePublicKeyAndToken();

                                    this.randomToken = values.randomToken;

                                    await this.SendPacket(new EncryptionRequest(values.publicKey, this.randomToken));

                                    break;
                                }

                                this.Player = new Player(UUIDFactory.CreateUUID(3, 1, $"OfflinePlayer:{username}"), username, this);

                                //await this.SetCompression();
                                await ConnectAsync();
                                break;
                            case 0x01:
                                var encryptionResponse = await PacketSerializer.DeserializeAsync<EncryptionResponse>(packet.data);

                                this.sharedKey = this.packetCryptography.Decrypt(encryptionResponse.SharedSecret);
                                var decryptedToken = this.packetCryptography.Decrypt(encryptionResponse.VerifyToken);

                                var decryptedTokenString = Convert.ToBase64String(decryptedToken);
                                var tokenString = Convert.ToBase64String(this.randomToken);

                                if (!decryptedTokenString.Equals(tokenString))
                                {
                                    await this.DisconnectAsync(ChatMessage.Simple("Invalid token.."));
                                    break;
                                }

                                var serverId = sharedKey.Concat(this.packetCryptography.PublicKey).ToArray().MinecraftShaDigest();

                                JoinedResponse response = await MinecraftAPI.HasJoined(this.Player.Username, serverId);

                                if (response is null)
                                {
                                    await this.Logger.LogWarningAsync($"Failed to auth {this.Player.Username}");
                                    await this.DisconnectAsync(ChatMessage.Simple("Unable to authenticate.."));
                                    break;
                                }

                                this.encryptionEnabled = true;
                                this.minecraftStream = new AesStream(this.debugStream ?? (Stream)this.tcp.GetStream(), this.sharedKey);

                                //await this.SetCompression();
                                await ConnectAsync();
                                break;
                            case 0x02:
                                // Login Plugin Response
                                break;
                        }
                        break;

                    case ClientState.Play:

                        ///this.Logger.LogDebugAsync($"Received Play packet with Packet ID 0x{packet.PacketId.ToString("X")}");

                        await PacketHandler.HandlePlayPackets(packet, this);
                        break;
                }
            }

            await Logger.LogMessageAsync($"Disconnected client");

            if (this.State == ClientState.Play)
                await this.Server.Events.InvokePlayerLeaveAsync(new PlayerLeaveEventArgs(this));

            if (tcp.Connected)
            {
                this.tcp.Close();

                if (this.Player != null)
                    this.Server.OnlinePlayers.TryRemove(this.Player.Uuid, out var _);
            }
        }

        private async Task ProcessQueue()
        {
            while (!Cancellation.IsCancellationRequested && this.tcp.Connected)
            {
                if (this.PacketQueue.TryDequeue(out var packet))
                {
                    await this.SendPacket(packet);
                    await Logger.LogWarningAsync($"Enqueued packet: {packet} (0x{packet.id:X2})");
                }
            }
        }

        //TODO fix compression
        private async Task SetCompression()
        {
            await this.SendPacket(new SetCompression(compressionThreshold));
            this.compressionEnabled = true;
            await this.Logger.LogDebugAsync("Compression has been enabled.");
        }

        private async Task ConnectAsync()
        {
            await this.SendPacket(new LoginSuccess(this.Player.Uuid, this.Player.Username));
            await this.Logger.LogDebugAsync($"Sent Login success to user {this.Player.Username} {this.Player.Uuid}");

            this.State = ClientState.Play;
            this.Player.Gamemode = Gamemode.Creative;

            this.Server.OnlinePlayers.TryAdd(this.Player.Uuid, this.Player);

            await this.SendPacket(new JoinGame
            {
                EntityId = (int)(EntityId.Player | (EntityId)this.id),
                GameMode = Gamemode.Creative,
                Dimension = Dimension.Overworld,
                Difficulty = Difficulty.Peaceful,
                ReducedDebugInfo = false
            });
            await this.Logger.LogDebugAsync("Sent Join Game packet.");

            await this.SendPacket(new SpawnPosition(new Position(0, 100, 0)));
            await this.Logger.LogDebugAsync("Sent Spawn Position packet.");

            await this.SendPacket(new PlayerPositionLook(new Transform(0, 105, 0), PositionFlags.NONE, 0));
            await this.Logger.LogDebugAsync("Sent Position packet.");

            await this.SendServerBrand();

            await this.Server.Events.InvokePlayerJoinAsync(new PlayerJoinEventArgs(this, DateTimeOffset.Now));

            await this.SendDeclareCommandsAsync();
            await this.SendPlayerInfoAsync();
            await this.SendPlayerListDecoration();

            await Server.world.ResendBaseChunksAsync(4, 0, 0, 0, 0, this);
        }

        private async Task SendServerBrand()
        {
            await using var stream = new MinecraftStream();
            await stream.WriteStringAsync("obsidian");
            await this.QueuePacketAsync(new PluginMessage("minecraft:brand", stream.ToArray()));
            await this.Logger.LogDebugAsync("Sent server brand.");
        }

        private async Task SendPlayerListDecoration()
        {
            var header = string.IsNullOrWhiteSpace(Server.Config.Header) ? null : ChatMessage.Simple(Server.Config.Header);
            var footer = string.IsNullOrWhiteSpace(Server.Config.Footer) ? null : ChatMessage.Simple(Server.Config.Footer);

            await this.SendPlayerListHeaderFooterAsync(header, footer);
            await this.Logger.LogDebugAsync("Sent player list decoration");
        }

        public async Task SendChunkAsync(Chunk chunk)
        {
            var chunkData = new ChunkDataPacket(chunk.X, chunk.Z);

            for (int i = 0; i < 16; i++)
                chunkData.Data.Add(new ChunkSection().FilledWithLight());

            for (int x = 0; x < 16; x++)
            {
                for (int y = 0; y < 16; y++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        var block = chunk.Blocks[x, y, z];

                        chunkData.Data[6].BlockStateContainer.Set(x, y, z, block);
                    }
                }
            }

            for (int i = 0; i < 16 * 16; i++)
                chunkData.Biomes.Add(29); //TODO: Add proper biomes

            await this.QueuePacketAsync(chunkData);
        }

        public Task UnloadChunkAsync(int x, int z) => this.QueuePacketAsync(new UnloadChunk(x, z));

        public async Task SendPacket(Packet packet)
        {
            if (this.compressionEnabled)
            {
                await packet.WriteCompressedAsync(minecraftStream, compressionThreshold);
            }
            else
            {
                await PacketSerializer.SerializeAsync(packet, this.minecraftStream);
            }
        }

        internal async Task QueuePacketAsync(Packet packet)
        {
            this.PacketQueue.Enqueue(packet);
            await Logger.LogWarningAsync($"Queuing packet: {packet} (0x{packet.id:X2})");
        }
        internal void Disconnect() => this.Cancellation.Cancel();

        #region dispose methods
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

        #endregion
    }
}