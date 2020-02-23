using Obsidian.Chat;
using Obsidian.ChunkData;
using Obsidian.Commands;
using Obsidian.Entities;
using Obsidian.Events.EventArgs;
using Obsidian.Logging;
using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Login;
using Obsidian.Net.Packets.Play;
using Obsidian.PlayerData;
using Obsidian.PlayerData.Info;
using Obsidian.Util;
using Obsidian.World;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Obsidian.Net.Packets.Handshaking;
using Obsidian.Net.Packets.Status;
using Obsidian.Util.DataTypes;
using Obsidian.Util.Mojang;

namespace Obsidian
{
    public class Client : IDisposable
    {
        private byte[] Token { get; set; }
        private byte[] SharedKey { get; set; }

        public int Ping { get; private set; }
        public int PlayerId { get; private set; }

        public TcpClient Tcp { get; private set; }
        public MinecraftStream MinecraftStream { get; set; }
        public CancellationTokenSource Cancellation { get; private set; }

        public ClientState State { get; private set; }

        private bool Disposed;
        private bool CompressionEnabled;
        private bool EncryptionEnabled;

        private const int CompressionThreshold = 256;

        public bool Playing => this.State == ClientState.Play && this.Player != null;
        public AsyncLogger Logger => this.OriginServer.Logger;


        public int MissedKeepalives;

        public Server OriginServer;
        public Player Player;
        public Config Config;
        public ClientSettings ClientSettings;

        public ConcurrentQueue<Packet> PacketQueue = new ConcurrentQueue<Packet>();

        public Client(TcpClient tcp, Config config, int playerId, Server originServer)
        {
            this.Tcp = tcp;
            this.Config = config;
            this.PlayerId = playerId;

            this.OriginServer = originServer;
            this.Cancellation = new CancellationTokenSource();
            this.State = ClientState.Handshaking;

            this.MinecraftStream = new MinecraftStream(tcp.GetStream());
        }

        ~Client()
        {
            Dispose(false);
        }

        #region Packet Sending Methods

        internal async Task DisconnectAsync(ChatMessage reason)
        {
            var packet = new Disconnect(reason, this.State);
            SendPacket(packet);
        }

        internal async Task ProcessKeepAlive(long id)
        {
            this.Ping = (int)(DateTime.Now.Millisecond - id);
            
            var packet = new KeepAlive(id);
            SendPacket(packet);
            
            MissedKeepalives += 1; // This will be decreased after an answer is received.
            if (MissedKeepalives > this.Config.MaxMissedKeepalives)
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
            var packet = new PlayerPositionLook(poslook, posflags, tpid);
            SendPacket(packet);
        }

        internal async Task SendBlockChangeAsync(BlockChange b)
        {
            this.Logger.LogMessage($"Sending block change to {Player.Username}");
            SendPacket(b);
            this.Logger.LogMessage($"Block change sent to {Player.Username}");
        }

        internal async Task SendSpawnMobAsync(int id, Guid uuid, int type, Transform transform, byte headPitch, Velocity velocity, Entity entity)
        {
            var packet = new SpawnMob(id, uuid, type, transform, headPitch, velocity, entity);
            SendPacket(packet);

            this.Logger.LogDebug($"Spawned entity with id {id} for player {this.Player.Username}");
        }

        internal async Task SendEntity(EntityPacket packet)
        {
            SendPacket(packet);
            this.Logger.LogDebug($"Sent entity with id {packet.Id} for player {this.Player.Username}");
        }

        internal async Task SendDeclareCommandsAsync()
        {
            var packet = new DeclareCommands();

            var node = new CommandNode()
            {
                Type = CommandNodeType.Root
            };
            foreach (Qmmands.Command command in this.OriginServer.Commands.GetAllCommands())
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

            this.SendPacket(packet);
            this.Logger.LogDebug("Sent Declare Commands packet.");
        }

        internal async Task SendPlayerInfoAsync()
        {
            var list = new List<PlayerInfoAction>();

            foreach (Client client in this.OriginServer.Clients)
            {
                if (!client.Playing)
                {
                    continue;
                }

                Player player = client.Player;

                list.Add(new PlayerInfoAddAction()
                {
                    Name = player.Username,
                    Uuid = player.Uuid,
                    Ping = client.Ping,
                    Gamemode = (int)Player.Gamemode,
                    DisplayName = ChatMessage.Simple(player.Username)
                });
            }

            var packet= new PlayerInfo(0, list);
            SendPacket(packet);
            this.Logger.LogDebug($"Sent Player Info packet from {this.Player.Username}");
        }

        internal async Task SendPlayerAsync(int id, Guid uuid, Transform pos)
        {
            var packet = new SpawnPlayer
            {
                Id = id,

                Uuid = uuid,

                Tranform = pos,

                Player = this.Player
            };

            SendPacket(packet);
            
            this.Logger.LogDebug("New player spawned!");
        }

        internal async Task SendPlayerAsync(int id, string uuid, Transform pos)
        {
            var packet = new SpawnPlayer
            {
                Id = id,

                Uuid3 = uuid,

                Tranform = pos,

                Player = this.Player
            };
            
            SendPacket(packet);
            
            this.Logger.LogDebug("New player spawned!");
        }

        internal async Task SendPlayerListHeaderFooterAsync(ChatMessage header, ChatMessage footer)
        {
            var packet =  new PlayerListHeaderFooter(header, footer);
            SendPacket(packet);
            this.Logger.LogDebug("Sent Player List Footer Header packet.");
        }

        #endregion Packet Sending Methods

        private async Task<Packet> GetNextPacketAsync()
        {
            if (this.CompressionEnabled)
            {
                return await PacketHandler.ReadCompressedPacketAsync(this.MinecraftStream);
            }
            else
            {
                return await PacketHandler.ReadPacketAsync(this.MinecraftStream);
            }
        }

        public async Task StartConnectionAsync()
        {
            Task.Run(EnqueuePacketsAsync);
            
            while (!Cancellation.IsCancellationRequested && this.Tcp.Connected)
            {
                Packet packet = await this.GetNextPacketAsync();

                if (this.State == ClientState.Play && packet.packetData.Length < 1)
                    this.Disconnect();

                switch (this.State)
                {
                    case ClientState.Status: //server ping/list
                        switch (packet.packetId)
                        {
                            case 0x00:
                                var status = new ServerStatus(OriginServer);
                                var requestResponse = new RequestResponse(status);
                                await requestResponse.WriteAsync(this.MinecraftStream);
                                break;

                            case 0x01:
                                var pingPong = new PingPong(packet.packetData);
                                await pingPong.WriteAsync(this.MinecraftStream);
                                this.Disconnect();
                                break;
                        }
                        break;

                    case ClientState.Handshaking:
                        if (packet.packetId == 0x00)
                        {
                            if (packet == null)
                                throw new InvalidOperationException();

                            var handshake = new Handshake(packet.packetData);
                            await handshake.ReadAsync();

                            var nextState = handshake.NextState;

                            if (nextState != ClientState.Status && nextState != ClientState.Login)
                            {
                                this.Logger.LogDebug($"Client sent unexpected state ({(int)nextState}), forcing it to disconnect");
                                await this.DisconnectAsync(ChatMessage.Simple("you seem suspicious"));
                            }

                            this.State = nextState;
                            this.Logger.LogMessage($"Handshaking with client (protocol: {handshake.Version}, server: {handshake.ServerAddress}:{handshake.ServerPort})");
                        }
                        else
                        {
                            //Handle legacy ping stuff
                        }
                        break;

                    case ClientState.Login:
                        switch (packet.packetId)
                        {
                            default:
                                this.Logger.LogError("Client in state Login tried to send an unimplemented packet. Forcing it to disconnect.");
                                await this.DisconnectAsync(ChatMessage.Simple("Unknown Packet Id."));
                                break;

                            case 0x00:
                                var loginStart = new LoginStart(packet.packetData);
                                await loginStart.ReadAsync(packet.packetData);

                                string username = loginStart.Username;

                                if (Config.MulitplayerDebugMode)
                                {
                                    username = $"Player{new Random().Next(1, 999)}";
                                    this.Logger.LogDebug($"Overriding username from {loginStart.Username} to {username}");
                                }

                                this.Logger.LogDebug($"Received login request from user {loginStart.Username}");

                                if (this.OriginServer.CheckPlayerOnline(username))
                                    await this.OriginServer.Clients.FirstOrDefault(c => c.Player.Username == username).DisconnectAsync(Chat.ChatMessage.Simple("Logged in from another location"));

                                if (this.Config.OnlineMode)
                                {
                                    var users = await MinecraftAPI.GetUsersAsync(new string[] { loginStart.Username });
                                    var uid = users.FirstOrDefault();

                                    var uuid = Guid.Parse(uid.Id);
                                    this.Player = new Player(uuid, loginStart.Username, this);

                                    PacketCryptography.GenerateKeyPair();

                                    var pubKey = PacketCryptography.PublicKeyToAsn();

                                    this.Token = PacketCryptography.GetRandomToken();

                                    var encryptionRequest = new EncryptionRequest(pubKey, this.Token);
                                    await encryptionRequest.WriteAsync(this.MinecraftStream);

                                    break;
                                }

                                this.Player = new Player(Guid.NewGuid(), username, this);

                                await this.SetCompression();
                                await ConnectAsync(this.Player.Uuid);
                                break;
                            case 0x01:
                                var encryptionResponse = new EncryptionResponse(packet.packetData);
                                await encryptionResponse.ReadAsync(packet.packetData);

                                JoinedResponse response;

                                this.SharedKey = PacketCryptography.Decrypt(encryptionResponse.SharedSecret);

                                var dec2 = PacketCryptography.Decrypt(encryptionResponse.VerifyToken);

                                var decBase64 = Convert.ToBase64String(dec2);

                                var tokenBase64 = Convert.ToBase64String(this.Token);

                                if (!decBase64.Equals(tokenBase64))
                                {
                                    await this.DisconnectAsync(ChatMessage.Simple("Invalid token.."));
                                    break;
                                }

                                var encodedKey = PacketCryptography.PublicKeyToAsn();

                                var serverId = PacketCryptography.MinecraftShaDigest(SharedKey.Concat(encodedKey).ToArray());

                                response = await MinecraftAPI.HasJoined(this.Player.Username, serverId);

                                if (response is null)
                                {
                                    this.Logger.LogWarning($"Failed to auth {this.Player.Username}");
                                    await this.DisconnectAsync(ChatMessage.Simple("Unable to authenticate.."));
                                    break;
                                }

                                this.EncryptionEnabled = true;
                                this.MinecraftStream = new AesStream(this.Tcp.GetStream(), this.SharedKey);

                                await this.SetCompression();
                                await ConnectAsync(this.Player.Uuid);
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

            Logger.LogMessage($"Disconnected client");

            if (this.Playing)
                await this.OriginServer.Events.InvokePlayerLeave(new PlayerLeaveEventArgs(this));

            this.Player = null;

            if (Tcp.Connected)
                this.Tcp.Close();
        }

        private async Task EnqueuePacketsAsync()
        {
            while (!Cancellation.IsCancellationRequested && this.Tcp.Connected)
            {
                if (!this.PacketQueue.Any()) continue;
                if (!this.PacketQueue.TryDequeue(out var outgoingPacket)) continue;
                Logger.LogDebug($"Enqueuing packet: {outgoingPacket} (0x{outgoingPacket.packetId:X2})");
                
                if (this.CompressionEnabled)
                {
                    await outgoingPacket.WriteCompressedAsync(MinecraftStream, CompressionThreshold);
                }
                else
                {
                    await outgoingPacket.WriteAsync(this.MinecraftStream);
                }
            }
        }

        private async Task SetCompression()
        {
            
            var packet = new SetCompression(CompressionThreshold);
            SendPacket(packet);
            
            this.CompressionEnabled = true;
            
            this.Logger.LogDebug("Compression has been enabled.");
        }

        private async Task ConnectAsync(Guid uuid)
        {
            this.SendPacket(new LoginSuccess(uuid, this.Player.Username));
            this.Logger.LogDebug($"Sent Login success to user {this.Player.Username} {this.Player.Uuid.ToString()}");

            this.State = ClientState.Play;
            this.Player.Gamemode = Gamemode.Creative;

            this.SendPacket(new JoinGame((int)(EntityId.Player | (EntityId)this.PlayerId), Gamemode.Creative, 0, 0, "default", true));
            this.Logger.LogDebug("Sent Join Game packet.");

            this.SendPacket(new SpawnPosition(new Position(0, 100, 0)));
            this.Logger.LogDebug("Sent Spawn Position packet.");

            this.SendPacket(new PlayerPositionLook(new Transform(0, 105, 0), PositionFlags.NONE, 0));
            this.Logger.LogDebug("Sent Position packet.");

            //await using var stream = new MinecraftStream();
            //await stream.WriteStringAsync("obsidian");
            //this.SendPacket(new PluginMessage("minecraft:brand", stream.ToArray()));


            this.Logger.LogDebug("Sent server brand.");

            await this.OriginServer.Events.InvokePlayerJoin(new PlayerJoinEventArgs(this, DateTimeOffset.Now));

            await this.SendDeclareCommandsAsync();
            //await this.SendPlayerInfoAsync();

            //await this.SendPlayerListHeaderFooterAsync(string.IsNullOrWhiteSpace(OriginServer.Config.Header) ? null : ChatMessage.Simple(OriginServer.Config.Header),
            //                                         string.IsNullOrWhiteSpace(OriginServer.Config.Footer) ? null : ChatMessage.Simple(OriginServer.Config.Footer));
            //this.Logger.LogDebug("Sent player list decoration");

            //await this.SendChunkAsync(OriginServer.WorldGenerator.GenerateChunk(new Chunk(0, 0)));
            //await this.SendChunkAsync(OriginServer.WorldGenerator.GenerateChunk(new Chunk(-1, 0)));
            //await this.SendChunkAsync(OriginServer.WorldGenerator.GenerateChunk(new Chunk(0, -1)));
            //await this.SendChunkAsync(OriginServer.WorldGenerator.GenerateChunk(new Chunk(-1, -1)));

            OriginServer.world.ResendBaseChunks(4, 0, 0, 0, 0, this);

            this.Logger.LogDebug("Sent chunk");

            /*if (this.OriginServer.Config.OnlineMode)
            {
                await this.OriginServer.SendNewPlayer(this.PlayerId, this.Player.Uuid, new Transform
                {
                    X = 0,

                    Y = 105,

                    Z = 0,

                    Pitch = 1,

                    Yaw = 1
                });
            }
            else
            {
                await this.OriginServer.SendNewPlayer(this.PlayerId, this.Player.Uuid3, new Transform
                {
                    X = 0,

                    Y = 105,

                    Z = 0,

                    Pitch = 1,

                    Yaw = 1
                });
            }*/
        }

        public void SendChunk(Chunk chunk)
        {
            var chunkData = new ChunkDataPacket(chunk.X, chunk.Z);

            for (int i = 0; i < 16; i++)
            {
                chunkData.Data.Add(new ChunkSection().FilledWithLight());
            }

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
            {
                chunkData.Biomes.Add(29); //TODO: Add proper biomes
            }

            this.SendPacket(chunkData);
        }

        internal void Disconnect() => this.Cancellation.Cancel();
        internal void SendPacket(Packet packet)
        {
            this.PacketQueue.Enqueue(packet);
            Logger.LogWarning($"Queuing packet: {packet} (0x{packet.packetId:X2})");
        }

        #region dispose methods
        protected virtual void Dispose(bool disposing)
        {
            if (this.Disposed)
                return;

            if (disposing)
            {
                this.MinecraftStream.Dispose();
                this.Tcp.Dispose();

                if (this.Cancellation != null)
                    this.Cancellation.Dispose();
            }

            this.MinecraftStream = null;
            this.Tcp = null;
            this.Cancellation = null;

            this.Token = null;
            this.SharedKey = null;
            this.Player = null;
            this.ClientSettings = null;
            this.Config = null;
            this.OriginServer = null;

            this.Disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);

            GC.SuppressFinalize(this);
        }

        #endregion
    }
}