using DaanV2.UUID;
using Microsoft.Extensions.Logging;
using Obsidian.Chat;
using Obsidian.ChunkData;
using Obsidian.Commands;
using Obsidian.Commands.Parsers;
using Obsidian.Entities;
using Obsidian.Events.EventArgs;
using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Handshaking;
using Obsidian.Net.Packets.Login;
using Obsidian.Net.Packets.Play;
using Obsidian.Net.Packets.Play.Client;
using Obsidian.Net.Packets.Play.Server;
using Obsidian.Net.Packets.Status;
using Obsidian.PlayerData;
using Obsidian.PlayerData.Info;
using Obsidian.Serializer;
using Obsidian.Util;
using Obsidian.Util.DataTypes;
using Obsidian.Util.Debug;
using Obsidian.Util.Extensions;
using Obsidian.Util.Mojang;
using Obsidian.WorldData;

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
        public bool isDragging;

        private const int compressionThreshold = 256;

        internal TcpClient tcp;

        internal int clickActionNumber;
        internal int ping;
        internal int missedKeepalives;
        internal int id;

        /// <summary>
        /// The client brand
        /// </summary>
        public string Brand { get; set; }

        public ClientSettings ClientSettings { get; internal set; }

        public CancellationTokenSource Cancellation { get; private set; } = new CancellationTokenSource();

        public ClientState State { get; private set; } = ClientState.Handshaking;

        public ConcurrentQueue<Packet> PacketQueue { get; } = new ConcurrentQueue<Packet>();

        public Server Server { get; private set; }
        public Player Player { get; private set; }

        public ILogger Logger => this.Server.Logger;

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
            this.Dispose(false);
        }

        private Task<Packet> GetNextPacketAsync() => this.compressionEnabled ? PacketHandler.ReadCompressedPacketAsync(this.minecraftStream) : PacketHandler.ReadPacketAsync(this.minecraftStream);

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
                                await this.SendPacketAsync(new RequestResponse(status));
                                break;

                            case 0x01:
                                await this.SendPacketAsync(new PingPong(packet.data));
                                this.Disconnect();
                                break;
                        }
                        break;

                    case ClientState.Handshaking:
                        if (packet.id == 0x00)
                        {
                            if (packet == null)
                                throw new InvalidOperationException();

                            var handshake = await PacketSerializer.FastDeserializeAsync<Handshake>(packet.data);

                            var nextState = handshake.NextState;

                            if (nextState != ClientState.Status && nextState != ClientState.Login)
                            {
                                this.Logger.LogDebug($"Client sent unexpected state ({(int)nextState}), forcing it to disconnect");
                                await this.DisconnectAsync(ChatMessage.Simple("you seem suspicious"));
                            }

                            this.State = nextState;
                            this.Logger.LogInformation($"Handshaking with client (protocol: {handshake.Version}, server: {handshake.ServerAddress}:{handshake.ServerPort})");
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
                                this.Logger.LogError("Client in state Login tried to send an unimplemented packet. Forcing it to disconnect.");
                                await this.DisconnectAsync(ChatMessage.Simple("Unknown Packet Id."));
                                break;

                            case 0x00:
                                var loginStart = await PacketSerializer.FastDeserializeAsync<LoginStart>(packet.data);

                                string username = config.MulitplayerDebugMode ? $"Player{Program.Random.Next(1, 999)}" : loginStart.Username;

                                this.Logger.LogDebug($"Received login request from user {loginStart.Username}");

                                await this.Server.DisconnectIfConnectedAsync(username);

                                if (this.config.OnlineMode)
                                {
                                    var user = await MinecraftAPI.GetUserAsync(loginStart.Username);

                                    this.Player = new Player(Guid.Parse(user.Id), loginStart.Username, this);

                                    this.packetCryptography.GenerateKeyPair();

                                    var values = this.packetCryptography.GeneratePublicKeyAndToken();

                                    this.randomToken = values.randomToken;

                                    await this.SendPacketAsync(new EncryptionRequest(values.publicKey, this.randomToken));

                                    break;
                                }

                                this.Player = new Player(UUIDFactory.CreateUUID(3, 1, $"OfflinePlayer:{username}"), username, this);

                                //await this.SetCompression();
                                await this.ConnectAsync();
                                break;
                            case 0x01:
                                var encryptionResponse = await PacketSerializer.FastDeserializeAsync<EncryptionResponse>(packet.data);

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
                                    this.Logger.LogWarning($"Failed to auth {this.Player.Username}");
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

                        //await this.Logger.LogDebugAsync($"Received Play packet with Packet ID 0x{packet.id.ToString("X")}");

                        await PacketHandler.HandlePlayPackets(packet, this);
                        break;
                }
            }

            Logger.LogInformation($"Disconnected client");

            if (this.State == ClientState.Play)
                await this.Server.Events.InvokePlayerLeaveAsync(new PlayerLeaveEventArgs(this.Player));

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
                    await this.SendPacketAsync(packet);
                    this.Logger.LogDebug($"Enqueued packet: {packet} (0x{packet.id:X2})");
                }

                await Task.Delay(50);
            }
        }

        //TODO fix compression
        private async Task SetCompression()
        {
            await this.SendPacketAsync(new SetCompression(compressionThreshold));
            this.compressionEnabled = true;
            this.Logger.LogDebug("Compression has been enabled.");
        }

        private async Task ConnectAsync()
        {
            await this.QueuePacketAsync(new LoginSuccess(this.Player.Uuid.ToString(), this.Player.Username));
            this.Logger.LogDebug($"Sent Login success to user {this.Player.Username} {this.Player.Uuid}");

            this.State = ClientState.Play;
            this.Player.Gamemode = Gamemode.Creative;

            this.Server.OnlinePlayers.TryAdd(this.Player.Uuid, this.Player);

            await this.QueuePacketAsync(new JoinGame
            {
                EntityId = this.id,
                GameMode = Gamemode.Survival,
                Dimension = Dimension.Overworld,
                HashedSeed = 0,//New field
                ReducedDebugInfo = false
            });

            this.Logger.LogDebug("Sent Join Game packet.");

            await this.QueuePacketAsync(new SpawnPosition(new Position(0, 100, 0)));
            this.Logger.LogDebug("Sent Spawn Position packet.");

            this.Player.Position = new Position(0, 6, 0);

            await this.QueuePacketAsync(new ClientPlayerPositionLook
            {
                Position = this.Player.Position,
                Yaw = 0,
                Pitch = 0,
                Flags = PositionFlags.NONE,
                TeleportId = 0
            });
            this.Logger.LogDebug("Sent Position packet.");

            await this.SendDeclareCommandsAsync();
            await this.SendPlayerInfoAsync();
            await this.SendPlayerListDecoration();
            await this.SendServerBrand();

            await this.Server.Events.InvokePlayerJoinAsync(new PlayerJoinEventArgs(this.Player, DateTimeOffset.Now));

            await this.LoadChunksAsync();

            //await Server.world.ResendBaseChunksAsync(4, 0, 0, 0, 0, this);//TODO fix its sending chunks too fast
        }

        #region Packet Sending Methods

        internal Task DisconnectAsync(ChatMessage reason) => this.SendPacketAsync(new Disconnect(reason, this.State));

        internal async Task ProcessKeepAlive(long id)
        {
            this.ping = (int)(DateTime.Now.Millisecond - id);
            await this.SendPacketAsync(new KeepAlive(id));
            this.missedKeepalives += 1; // This will be decreased after an answer is received.
            if (this.missedKeepalives > this.config.MaxMissedKeepalives)
            {
                // Too many keepalives missed, kill this connection.
                this.Cancellation.Cancel();
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

        internal async Task SendDeclareCommandsAsync()
        {
            var packet = new DeclareCommands();
            var index = 0;

            var node = new CommandNode()
            {
                Type = CommandNodeType.Root
            };

            foreach (Qmmands.Command command in this.Server.Commands.GetAllCommands())
            {
                var commandNode = new CommandNode()
                {
                    Name = command.Name,
                    Type = CommandNodeType.Literal,
                    Index = ++index
                };
                foreach (Qmmands.Parameter parameter in command.Parameters)
                {
                    var parameterNode = new CommandNode()
                    {
                        Name = parameter.Name,
                        Type = CommandNodeType.Argument,
                        Index = ++index
                    };
                    Type type = parameter.Type;

                    if (type == typeof(string))
                        parameterNode.Parser = new StringCommandParser(parameter.IsRemainder ? StringType.GreedyPhrase : StringType.SingleWord);
                    else if (type == typeof(double))
                        parameterNode.Parser = new CommandParser("brigadier:double");
                    else if (type == typeof(float))
                        parameterNode.Parser = new CommandParser("brigadier:float");
                    else if (type == typeof(int))
                        parameterNode.Parser = new CommandParser("brigadier:integer");
                    else if (type == typeof(bool))
                        parameterNode.Parser = new CommandParser("brigadier:bool");
                    else if (type == typeof(Position))
                        parameterNode.Parser = new CommandParser("minecraft:vec3");
                    else if (type == typeof(Player))
                        parameterNode.Parser = new EntityCommandParser(EntityCommadBitMask.OnlyPlayers);
                    else
                        continue;

                    commandNode.AddChild(parameterNode);
                }

                commandNode.Type |= CommandNodeType.IsExecutabe;

                node.AddChild(commandNode);
            }

            packet.AddNode(node);
            await this.QueuePacketAsync(packet);
            this.Logger.LogDebug("Sent Declare Commands packet.");
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

            await this.QueuePacketAsync(new PlayerInfo(4, list));
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

            await this.QueuePacketAsync(new PlayerInfo(0, list));
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
                    var uuid = player.Uuid.ToString().Replace("-", "");
                    var skin = await MinecraftAPI.GetUserAndSkinAsync(uuid);
                    piaa.Properties.AddRange(skin.Properties);
                }

                list.Add(piaa);
            }

            await this.QueuePacketAsync(new PlayerInfo(0, list));
        }

        internal async Task SendPacketAsync(Packet packet)
        {
            if (this.compressionEnabled)
            {
                await packet.WriteCompressedAsync(minecraftStream, compressionThreshold);
            }
            else
            {
                if (packet is ChunkDataPacket chunk)
                {
                    await chunk.WriteAsync(this.minecraftStream);

                    return;
                }
                await PacketSerializer.SerializeAsync(packet, this.minecraftStream);
            }
        }

        internal async Task QueuePacketAsync(Packet packet)
        {
            var args = await this.Server.Events.InvokeQueuePacketAsync(new QueuePacketEventArgs(this, packet));

            if (args.Cancel)
            {
                this.Logger.LogDebug("A packet was set to queue but an event handler prevented it.");
                return;
            }

            this.PacketQueue.Enqueue(packet);
            this.Logger.LogDebug($"Queuing packet: {packet} (0x{packet.id:X2})");
        }

        internal async Task LoadChunksAsync()
        {
            foreach (var chunk in this.Server.World.LoadedChunks)
                await this.QueuePacketAsync(new ChunkDataPacket(chunk));
        }

        internal Task SendChunkAsync(Chunk chunk) => this.QueuePacketAsync(new ChunkDataPacket(chunk));

        private async Task SendServerBrand()
        {
            await using var stream = new MinecraftStream();
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

        private Task UnloadChunkAsync(int x, int z) => this.QueuePacketAsync(new UnloadChunk(x, z));

        #endregion Packet Sending Methods

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