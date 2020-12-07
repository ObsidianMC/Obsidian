using DaanV2.UUID;
using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.Chat;
using Obsidian.CommandFramework.Attributes;
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
using Obsidian.Net.Packets.Play.Client.GameState;
using Obsidian.Net.Packets.Play.Server;
using Obsidian.Net.Packets.Status;
using Obsidian.PlayerData.Info;
using Obsidian.Serializer;
using Obsidian.Util;
using Obsidian.Util.Debug;
using Obsidian.Util.Extensions;
using Obsidian.Util.Mojang;
using Obsidian.Util.Registry;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

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

        private BufferBlock<IPacket> packetQueue;

        public Server Server { get; private set; }
        public Player Player { get; private set; }

        public ILogger Logger => this.Server.Logger;

        public List<(int, int)> LoadedChunks { get; internal set; }

        public Client(TcpClient tcp, Config config, int playerId, Server originServer)
        {
            this.tcp = tcp;
            this.config = config;
            this.id = playerId;
            this.packetCryptography = new PacketCryptography();
            this.Server = originServer;
            this.LoadedChunks = new List<(int cx, int cz)>();

            Stream parentStream = this.tcp.GetStream();
#if DEBUG
            //parentStream = this.DebugStream = new PacketDebugStream(parentStream);
#endif
            this.minecraftStream = new MinecraftStream(parentStream);

            var blockOptions = new ExecutionDataflowBlockOptions() { CancellationToken = Cancellation.Token, EnsureOrdered = true };
            packetQueue = new BufferBlock<IPacket>(blockOptions);
            var sendPacketBlock = new ActionBlock<IPacket>(async packet =>
            {
                try
                {
                    if (tcp.Connected)
                        await SendPacketAsync(packet);
                }
                catch (Exception e)
                {
                    if (Globals.Config.VerboseLogging)
                        Logger.LogError(e.Message + "\n" + e.StackTrace);
                }
            },
            blockOptions);

            var linkOptions = new DataflowLinkOptions() { PropagateCompletion = true };
            packetQueue.LinkTo(sendPacketBlock, linkOptions);
        }

        private async Task<(int id, byte[] data)> GetNextPacketAsync()
        {
            int length = await this.minecraftStream.ReadVarIntAsync();
            byte[] receivedData = new byte[length];

            await this.minecraftStream.ReadAsync(receivedData, 0, length);

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
                    await packetStream.ReadAsync(packetData, 0, packetData.Length);
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

                                await this.SendPacketAsync(new RequestResponse(status));
                                break;

                            case 0x01:
                                var pong = await PacketSerializer.FastDeserializeAsync<PingPong>(data);

                                await this.SendPacketAsync(pong);

                                this.Disconnect();
                                break;
                        }
                        break;

                    case ClientState.Handshaking:
                        if (id == 0x00)
                        {
                            var handshake = await PacketSerializer.FastDeserializeAsync<Handshake>(data);

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
                                var loginStart = await PacketSerializer.FastDeserializeAsync<LoginStart>(data);

                                string username = config.MulitplayerDebugMode ? $"Player{Globals.Random.Next(1, 999)}" : loginStart.Username;

                                this.Logger.LogDebug($"Received login request from user {loginStart.Username}");

                                await this.Server.DisconnectIfConnectedAsync(username);

                                if (this.config.OnlineMode)
                                {
                                    var user = await MinecraftAPI.GetUserAsync(loginStart.Username);

                                    this.Player = new Player(Guid.Parse(user.Id), loginStart.Username, this)
                                    {
                                        World = this.Server.World
                                    };

                                    this.packetCryptography.GenerateKeyPair();

                                    var values = this.packetCryptography.GeneratePublicKeyAndToken();

                                    this.randomToken = values.randomToken;

                                    await this.SendPacketAsync(new EncryptionRequest(values.publicKey, this.randomToken));

                                    break;
                                }

                                this.Player = new Player(UUIDFactory.CreateUUID(3, 1, $"OfflinePlayer:{username}"), username, this)
                                {
                                    World = this.Server.World
                                };

                                //await this.SetCompression();
                                await this.ConnectAsync();
                                break;
                            case 0x01:
                                var encryptionResponse = PacketSerializer.FastDeserialize<EncryptionResponse>(data);

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

                        await PacketHandler.HandlePlayPackets((id, data), this);
                        break;
                }

                //await Task.Delay(50);
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

        // TODO fix compression
        private async Task SetCompressionAsync()
        {
            await this.SendPacketAsync(new SetCompression(compressionThreshold));
            this.compressionEnabled = true;
            this.Logger.LogDebug("Compression has been enabled.");
        }

        private Task DeclareRecipes() => this.QueuePacketAsync(new DeclareRecipes
        {
            RecipesLength = Registry.Recipes.Values.Count,

            Recipes = Registry.Recipes
        });

        private async Task ConnectAsync()
        {
            await this.QueuePacketAsync(new LoginSuccess(this.Player.Uuid, this.Player.Username));
            this.Logger.LogDebug($"Sent Login success to user {this.Player.Username} {this.Player.Uuid}");

            this.State = ClientState.Play;
            this.Player.Gamemode = Gamemode.Creative;

            this.Server.OnlinePlayers.TryAdd(this.Player.Uuid, this.Player);

            Registry.DefaultDimensions.TryGetValue(0, out var codec); // TODO support custom dimensions and savve client dimensionns

            await this.QueuePacketAsync(new JoinGame
            {
                EntityId = this.id,

                Gamemode = Gamemode.Creative,

                WorldCount = 1,
                WorldNames = new List<string> { "minecraft:world" },

                Codecs = new MixedCodec
                {
                    Dimensions = Registry.DefaultDimensions,
                    Biomes = Registry.DefaultBiomes
                },

                Dimension = codec,

                DimensionName = codec.Name,

                HashedSeed = 0,

                ReducedDebugInfo = false,

                EnableRespawnScreen = true,

                Flat = true
            });

            await this.SendServerBrand();

            // TODO figure out why tags make air blocks a fluid
            /*await this.QueuePacketAsync(new TagsPacket
            {
                Blocks = Registry.Tags["blocks"],

                Items = Registry.Tags["items"],

                Fluid = Registry.Tags["fluids"],

                Entities = Registry.Tags["entity_types"]
            });*/

            //TODO: Finish recipes
            await this.DeclareRecipes();

            await this.SendDeclareCommandsAsync();

            await this.QueuePacketAsync(new UnlockRecipes
            {
                Action = UnlockRecipeAction.Init,
                FirstRecipeIds = Registry.Recipes.Keys.ToList(),
                SecondRecipeIds = Registry.Recipes.Keys.ToList()
            });

            await this.SendPlayerInfoAsync();
            await this.SendPlayerListDecoration();

            await this.Server.Events.InvokePlayerJoinAsync(new PlayerJoinEventArgs(this.Player, DateTimeOffset.Now));

            await this.LoadChunksAsync();

            //TODO: check for last position
            var spawnPosition = new Position(
                Server.World.Data.SpawnX,
                Server.World.Data.SpawnY,
                Server.World.Data.SpawnZ);

            await this.QueuePacketAsync(new SpawnPosition(spawnPosition));
            this.Logger.LogDebug("Sent Spawn Position packet.");

            this.Logger.LogDebug("Sent Join Game packet.");

            this.Player.Location = spawnPosition;

            await this.QueuePacketAsync(new ClientPlayerPositionLook
            {
                Position = this.Player.Location,
                Yaw = 0,
                Pitch = 0,
                Flags = PositionFlags.NONE,
                TeleportId = 0
            });
            this.Logger.LogDebug("Sent Position packet.");

            // TODO fix its sending chunks too fast
            //await Server.world.ResendBaseChunksAsync(4, 0, 0, 0, 0, this);
        }

        #region Packet Sending Methods

        internal Task DisconnectAsync(ChatMessage reason) => this.SendPacketAsync(new Disconnect(reason, this.State));

        internal async Task ProcessKeepAliveAsync(long id)
        {
            this.ping = (int)(DateTime.Now.Millisecond - id);
            await this.SendPacketAsync(new KeepAlive(id));
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

        internal async Task SendDeclareCommandsAsync()
        {
            // TODO only build packet for first player, or prebuild packet. Very unlikely to add commands after server start??
            var packet = new DeclareCommands();
            var index = 0;

            var node = new CommandNode()
            {
                Type = CommandNodeType.Root,
                Index = index
            };

            foreach (var cmd in this.Server.Commands.GetAllCommands())
            {
                var cmdnode = new CommandNode()
                {
                    Index = ++index,
                    Name = cmd.Name,
                    Type = CommandNodeType.Literal
                };
                node.AddChild(cmdnode);

                foreach (var overload in cmd.Overloads.Take(1))
                {
                    var args = overload.GetParameters().Skip(1); // skipping obsidian context
                    if (args.Count() < 1)
                        cmdnode.Type |= CommandNodeType.IsExecutabe;

                    var prev = cmdnode;

                    foreach (var arg in args)
                    {
                        var argnode = new CommandNode()
                        {
                            Index = ++index,
                            Name = arg.Name,
                            Type = CommandNodeType.Argument | CommandNodeType.IsExecutabe
                        };

                        prev.AddChild(argnode);
                        prev = argnode;

                        Type type = arg.ParameterType;

                        var mctype = this.Server.Commands.FindMinecraftType(type);

                        switch (mctype)
                        {
                            case "brigadier:string":
                                argnode.Parser = new StringCommandParser(arg.CustomAttributes.Any(x => x.AttributeType == typeof(RemainingAttribute)) ? StringType.GreedyPhrase : StringType.QuotablePhrase);
                                break;
                            case "obsidian:player":
                                // this is a custom type used by obsidian meaning "only player entities".
                                argnode.Parser = new EntityCommandParser(EntityCommadBitMask.OnlyPlayers);
                                break;
                            default:
                                argnode.Parser = new CommandParser(mctype);
                                break;
                        }
                    }
                }
            }

            packet.AddNode(node);
            await this.QueuePacketAsync(packet);
            this.Logger.LogDebug("Sent Declare Commands packet.");
        }

        internal async Task RemovePlayerFromListAsync(IPlayer player)
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

        internal async Task AddPlayerToListAsync(IPlayer player)
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

        internal async Task SendPacketAsync(IPacket packet)
        {
            try
            {
                if (this.compressionEnabled)
                {
                    //await packet.WriteCompressedAsync(minecraftStream, compressionThreshold);//TODO
                }
                else
                {
                    await PacketSerializer.SerializeAsync(packet, this.minecraftStream);
                }
            }
            catch (Exception) { } // when packets are interrupted, threads may hang..
        }

        internal async Task QueuePacketAsync(IPacket packet)
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

        internal async Task LoadChunksAsync()
        {
            await this.Player.World.ResendBaseChunksAsync(this);
        }

        internal async Task SendChunkAsync(Chunk chunk)
        {
            if (chunk != null)
            {
                if (!this.LoadedChunks.Contains((chunk.X, chunk.Z)))
                {
                    await this.QueuePacketAsync(new ChunkDataPacket(chunk));
                }
            }
        }

        public async Task UnloadChunkAsync(int x, int z)
        {
            if (this.LoadedChunks.Contains((x, z)))
            {
                await this.QueuePacketAsync(new UnloadChunk(x, z));
            }
        }

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

        #endregion Packet Sending Methods

        internal void Disconnect() => this.Cancellation.Cancel();

        #region Dispose methods
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

        ~Client()
        {
            this.Dispose(false);
        }
    }
}