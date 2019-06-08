using Obsidian.Boss;
using Obsidian.Entities;
using Obsidian.Events.EventArgs;
using Obsidian.Logging;
using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.PlayerData.Info;
using Obsidian.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Obsidian
{
    public class Client
    {
        private readonly bool Compressed = false;

        public MinecraftStream MinecraftStream { get; set; }
        //private AesStream EncryptedStream { get; set; }

        public CancellationTokenSource Cancellation { get; private set; }

        public Server OriginServer;
        public TcpClient Tcp;
        public Player Player;
        public Config Config;
        public ClientSettings ClientSettings;

        public int Ping;
        //public int KeepAlives;
        public int PlayerId;

        public byte[] SharedKey = null;

        public bool Timedout = false;
        public bool EncryptionEnabled = false;

        public PacketState State { get; private set; }

        public Client(TcpClient tcp, Config config, int playerId, Server originServer)
        {
            this.Tcp = tcp;
            this.Config = config;
            this.PlayerId = playerId;

            this.OriginServer = originServer;
            this.Cancellation = new CancellationTokenSource();
            this.State = PacketState.Handshaking;

            this.MinecraftStream = new MinecraftStream(tcp.GetStream());
        }

        public Logger Logger => this.OriginServer.Logger;

        #region Packet Sending Methods
        public async Task DisconnectAsync(Chat.ChatMessage reason)
        {
            await Packet.CreateAsync(new Disconnect(reason, this.State), this.MinecraftStream);
        }

        public async Task SendChatAsync(string message, byte position = 0)
        {
            var chat = Chat.ChatMessage.Simple(message);
            //var pack = new Packet(0x0E, await new ChatMessage(chat, position).ToArrayAsync());
            await Packet.CreateAsync(new ChatMessagePacket(chat, position), this.MinecraftStream);
        }

        /// <summary>
        /// Sends KeepAlive
        /// </summary>
        /// <param name="id">ID for the keepalive. Just keep increasing this by 1, easiest approach.</param>
        public async Task SendKeepAliveAsync(long id)
        {
            //this.KeepAlives++;
            await Packet.CreateAsync(new KeepAlive(id), new MinecraftStream(this.Tcp.GetStream()));
        }

        public async Task SendSoundEffectAsync(int soundId, Location location, SoundCategory category = SoundCategory.Master, float pitch = 1.0f, float volume = 1f)
        {
            await Packet.CreateAsync(new SoundEffect(soundId, location, category, pitch, volume), this.MinecraftStream);
        }

        public async Task SendDeclareCommandsAsync()
        {
            await this.Logger.LogMessageAsync("Generating Declare Commands packet.");

            var packet = await Packet.CreateAsync(new DeclareCommands());

            foreach (Qmmands.Command command in this.OriginServer.Commands.GetAllCommands())
            {
                var commandNode = new Commands.CommandNode()
                {
                    Name = command.Name,
                    Type = Commands.CommandNodeType.Literal
                };

                foreach (Qmmands.Parameter parameter in command.Parameters)
                {
                    var parameterNode = new Commands.CommandNode()
                    {
                        Name = parameter.Name,
                        Type = Commands.CommandNodeType.Argument,
                    };

                    Type type = parameter.Type;

                    if (type == typeof(string)) parameterNode.Identifier = "brigadier:string";
                    else if (type == typeof(int)) parameterNode.Identifier = "brigadier:integer";
                    else if (type == typeof(bool)) parameterNode.Identifier = "brigadier:bool";
                    else throw new NotImplementedException("Not supported parameter");

                    commandNode.Children.Add(parameterNode);
                }

                if (commandNode.Children.Count > 0)
                {
                    commandNode.Children[0].Type |= Commands.CommandNodeType.IsExecutabe;
                }
                else
                {
                    commandNode.Type |= Commands.CommandNodeType.IsExecutabe;
                }

                packet.AddNode(commandNode);
            }

            await this.Logger.LogMessageAsync("Sending Declare Commands packet.");

            await packet.WriteToStreamAsync(this.MinecraftStream);
        }

        public async Task SendBossBarAsync(Guid uuid, BossBarAction action)
        {
            await Packet.CreateAsync(new BossBar(uuid, action), this.MinecraftStream);
        }

        public async Task SendPlayerInfoAsync()
        {
            await this.Logger.LogMessageAsync("Generating Player Info packet.");

            var list = new List<PlayerInfoAction>();

            foreach (Client client in this.OriginServer.Clients)
            {
                await this.Logger.LogMessageAsync("Looping");
                //BUG: Clients still has disconnected clients this HAS to be fixed.
                if (client.Player == null)
                {
                    continue;
                }

                list.Add(new PlayerInfoAddAction()
                {
                    Name = client.Player.Username,
                    UUID = client.Player.UUID,
                    Ping = 0,
                    Gamemode = client.Player.PlayerGameType
                });
            }

            await Packet.CreateAsync(new PlayerInfo(0, list), this.MinecraftStream);

            await this.Logger.LogMessageAsync("Sending Player Info packet.");
        }

        #endregion

        private async Task<CompressedPacket> GetNextCompressedPacketAsync()
        {
            return await CompressedPacket.ReadFromStreamAsync(this.MinecraftStream);
        }

        private async Task<Packet> GetNextPacketAsync()
        {
            return await Packet.ReadFromStreamAsync(this.MinecraftStream);
        }

        private byte[] Token { get; set; }

        public async Task StartConnectionAsync()
        {
            while (!Cancellation.IsCancellationRequested && this.Tcp.Connected)// I'm sure
            {
                Packet packet;
                Packet returnPacket;

                if (this.Compressed)
                    packet = await this.GetNextCompressedPacketAsync();
                else
                    packet = await this.GetNextPacketAsync();

                if (this.State == PacketState.Play && packet._packetData.Length < 1)
                    this.Disconnect();;

                switch (this.State)
                {
                    case PacketState.Status: //server ping/list
                        switch (packet.PacketId)
                        {
                            case 0x00:
                                // Request
                                await Packet.CreateAsync(new RequestResponse(ServerStatus.DebugStatus), this.MinecraftStream);
                                break;

                            case 0x01:
                                // Ping
                                returnPacket = await Packet.CreateAsync(new PingPong(packet._packetData));

                                await returnPacket.WriteToStreamAsync(this.MinecraftStream);
                                this.Disconnect();
                                break;
                        }
                        break;
                    case PacketState.Handshaking:
                        if (packet.PacketId == 0x00)
                        {
                            // Handshake
                            if (packet == null)
                                throw new InvalidOperationException();

                            var handshake = await Packet.CreateAsync(new Handshake(packet._packetData));

                            var nextState = handshake.NextState;

                            if (nextState != PacketState.Status && nextState != PacketState.Login)
                            {
                                await this.Logger.LogMessageAsync($"Client sent unexpected state ({(int)nextState}), forcing it to disconnect");
                                await this.DisconnectAsync(new Chat.ChatMessage() { Text = "you seem suspicious" });
                            }

                            this.State = nextState;
                            await this.Logger.LogMessageAsync($"Handshaking with client (protocol: {handshake.Version}, server: {handshake.ServerAddress}:{handshake.ServerPort})");
                        }
                        else
                        {
                            //Handle legacy ping stuff
                        }
                        break;
                    case PacketState.Login:
                        switch (packet.PacketId)
                        {
                            default:
                                await this.Logger.LogMessageAsync($"Client in state Login tried to send an unimplemented packet. Forcing it to disconnect.");
                                await this.DisconnectAsync(new Chat.ChatMessage()
                                {
                                    Text = this.Config.JoinMessage
                                });
                                break;

                            case 0x00:
                                var loginStart = await Packet.CreateAsync(new LoginStart(packet._packetData));

                                await this.Logger.LogMessageAsync($"Received login request from user {loginStart.Username}");

                                if (this.OriginServer.CheckPlayerOnline(loginStart.Username))
                                    await this.OriginServer.Clients.FirstOrDefault(c => c.Player.Username == loginStart.Username).DisconnectAsync(Chat.ChatMessage.Simple("Logged in from another location"));

                                var users = await MinecraftAPI.GetUsersAsync(new string[] { loginStart.Username });
                                var uid = users.FirstOrDefault();

                                var uuid = Guid.Parse(uid.Id);
                                this.Player = new Player(uuid, loginStart.Username);

                                if (this.Config.OnlineMode)
                                {
                                    PacketCryptography.GenerateKeyPair();

                                    var pubKey = PacketCryptography.PublicKeyToAsn();

                                    this.Token = PacketCryptography.GetRandomToken();

                                    returnPacket = await Packet.CreateAsync(new EncryptionRequest(pubKey, this.Token), this.MinecraftStream);

                                    break;
                                }

                                await ConnectAsync(this.Player.UUID, packet);

                                break;

                            case 0x01:
                                var encryptionResponse = await Packet.CreateAsync(new EncryptionResponse(packet._packetData));

                                JoinedResponse response;

                                try
                                {
                                    this.SharedKey = PacketCryptography.Decrypt(encryptionResponse.SharedSecret);

                                    var dec2 = PacketCryptography.Decrypt(encryptionResponse.VerifyToken);

                                    var dec2Base64 = Convert.ToBase64String(dec2);

                                    var tokenBase64 = Convert.ToBase64String(this.Token);

                                    if (!dec2Base64.Equals(tokenBase64))
                                    {
                                        await this.DisconnectAsync(Chat.ChatMessage.Simple("Invalid token.."));
                                        break;
                                    }

                                    var encodedKey = PacketCryptography.PublicKeyToAsn();

                                    var serverId = PacketCryptography.MinecraftShaDigest(SharedKey.Concat(encodedKey).ToArray());

                                    response = await MinecraftAPI.HasJoined(this.Player.Username, serverId);

                                    if (response is null)
                                    {
                                        await this.Logger.LogMessageAsync($"Failed to auth {this.Player.Username}");
                                        await this.DisconnectAsync(Chat.ChatMessage.Simple("Unable to authenticate.."));
                                    }
                                    this.EncryptionEnabled = true;
                                    this.MinecraftStream = new AesStream(this.Tcp.GetStream(), this.SharedKey);
                                }
                                catch
                                {
                                    throw;
                                }

                                await ConnectAsync(new Guid(response.Id), packet);
                                break;

                            case 0x02:
                                // Login Plugin Response
                                break;
                        }
                        break;
                    case PacketState.Play: // Gameplay packets. Put this last because the list is the longest.
                        await this.Logger.LogMessageAsync($"Received Play packet with Packet ID 0x{packet.PacketId.ToString("X")}");
                        switch (packet.PacketId)
                        {
                            case 0x00:
                                // Teleport Confirm
                                // GET X Y Z FROM PACKET TODO
                                //this.Player.Position = new Position((int)x, (int)y, (int)z);
                                await this.Logger.LogMessageAsync("Received teleport confirm");
                                break;

                            case 0x01:
                                // Query Block NBT
                                await this.Logger.LogMessageAsync("Received query block nbt");
                                break;

                            case 0x02:
                                // Incoming chat message
                                var message = await Packet.CreateAsync(new IncomingChatMessage(packet._packetData));
                                await this.Logger.LogMessageAsync($"received chat: {message.Message}");

                                await this.OriginServer.SendChatAsync(message.Message, this);
                                break;

                            case 0x03:
                                // Client status
                                await this.Logger.LogMessageAsync("Received client status");
                                break;

                            case 0x04:
                                // Client Settings
                                var settings = await Packet.CreateAsync(new ClientSettings(packet._packetData));
                                this.ClientSettings = settings;
                                await this.Logger.LogMessageAsync("Received client settings");
                                break;

                            case 0x05:
                                // Tab-Complete
                                await this.Logger.LogMessageAsync("Received tab-complete");
                                break;

                            case 0x06:
                                // Confirm Transaction
                                await this.Logger.LogMessageAsync("Received confirm transaction");
                                break;

                            case 0x07:
                                // Enchant Item
                                await this.Logger.LogMessageAsync("Received enchant item");
                                break;

                            case 0x08:
                                // Click Window
                                await this.Logger.LogMessageAsync("Received click window");
                                break;

                            case 0x09:
                                // Close Window (serverbound)
                                await this.Logger.LogMessageAsync("Received close window");
                                break;

                            case 0x0A:
                                // Plugin Message (serverbound)
                                await this.Logger.LogMessageAsync("Received plugin message");
                                break;

                            case 0x0B:
                                // Edit Book
                                await this.Logger.LogMessageAsync("Received edit book");
                                break;

                            case 0x0C:
                                // Query Entity NBT
                                await this.Logger.LogMessageAsync("Received query entity nbt");
                                break;

                            case 0x0D:
                                // Use Entity
                                await this.Logger.LogMessageAsync("Received use entity");
                                break;

                            case 0x0E:
                                // Keep Alive (serverbound)
                                var keepalive = await Packet.CreateAsync(new KeepAlive(packet._packetData));

                                // Check whether keepalive id has been sent
                                await this.Logger.LogMessageAsync($"Successfully kept alive player {this.Player.Username} with ka id {keepalive.KeepAliveId}");
                                break;

                            case 0x0F:
                                // Player
                                var onground = BitConverter.ToBoolean(await packet.ToArrayAsync(), 0);
                                await this.Logger.LogMessageAsync($"{this.Player.Username} on ground?: {onground}");
                                this.Player.OnGround = onground;
                                break;

                            case 0x10:
                                // Player Position 
                                var pos = await Packet.CreateAsync(new PlayerPosition(packet._packetData));

                                this.Player.Location.X = pos.X;
                                this.Player.Location.Y = pos.Y;
                                this.Player.Location.Z = pos.Z;
                                this.Player.OnGround = pos.OnGround;
                                await this.Logger.LogMessageAsync($"Updated position for {this.Player.Username}");
                                break;

                            case 0x11:
                                // Player Position And Look (serverbound)
                                var ppos = await Packet.CreateAsync(new PlayerPositionLook(packet._packetData));

                                this.Player.Location.X = ppos.X;
                                this.Player.Location.Y = ppos.Y;
                                this.Player.Location.Z = ppos.Z;
                                this.Player.Location.Yaw = ppos.Yaw;
                                this.Player.Location.Pitch = ppos.Pitch;
                                await this.Logger.LogMessageAsync($"Updated look and position for {this.Player.Username}");
                                break;

                            case 0x12:
                                // Player Look
                                var look = await Packet.CreateAsync(new PlayerLook(packet._packetData));

                                this.Player.Location.Yaw = look.Yaw;
                                this.Player.Location.Pitch = look.Pitch;
                                this.Player.OnGround = look.OnGround;
                                await this.Logger.LogMessageAsync($"Updated look for {this.Player.Username}");
                                break;

                            case 0x13:
                                // Vehicle Move (serverbound)
                                await this.Logger.LogMessageAsync("Received vehicle move");
                                break;

                            case 0x14:
                                // Steer Boat
                                await this.Logger.LogMessageAsync("Received steer boat");
                                break;

                            case 0x15:
                                // Pick Item
                                await this.Logger.LogMessageAsync("Received pick item");
                                break;

                            case 0x16:
                                // Craft Recipe Request
                                await this.Logger.LogMessageAsync("Received craft recipe request");
                                break;

                            case 0x17:
                                // Player Abilities (serverbound)
                                await this.Logger.LogMessageAsync("Received player abilities");
                                break;

                            case 0x18:
                                // Player Digging
                                await this.Logger.LogMessageAsync("Received player digging");
                                break;

                            case 0x19:
                                // Entity Action
                                await this.Logger.LogMessageAsync("Received entity action");
                                break;

                            case 0x1A:
                                // Steer Vehicle
                                await this.Logger.LogMessageAsync("Received steer vehicle");
                                break;

                            case 0x1B:
                                // Recipe Book Data
                                await this.Logger.LogMessageAsync("Received recipe book data");
                                break;

                            case 0x1C:
                                // Name Item
                                await this.Logger.LogMessageAsync("Received name item");
                                break;

                            case 0x1D:
                                // Resource Pack Status
                                await this.Logger.LogMessageAsync("Received resource pack status");
                                break;

                            case 0x1E:
                                // Advancement Tab
                                await this.Logger.LogMessageAsync("Received advancement tab");
                                break;

                            case 0x1F:
                                // Select Trade
                                await this.Logger.LogMessageAsync("Received select trade");
                                break;

                            case 0x20:
                                // Set Beacon Effect
                                await this.Logger.LogMessageAsync("Received set beacon effect");
                                break;

                            case 0x21:
                                // Held Item Change (serverbound)
                                await this.Logger.LogMessageAsync("Received held item change");
                                break;

                            case 0x22:
                                // Update Command Block
                                await this.Logger.LogMessageAsync("Received update command block");
                                break;

                            case 0x23:
                                // Update Command Block Minecart
                                await this.Logger.LogMessageAsync("Received update command block minecart");
                                break;

                            case 0x24:
                                // Creative Inventory Action
                                await this.Logger.LogMessageAsync("Received creative inventory action");
                                break;

                            case 0x25:
                                // Update Structure Block
                                await this.Logger.LogMessageAsync("Received update structure block");
                                break;

                            case 0x26:
                                // Update Sign
                                await this.Logger.LogMessageAsync("Received update sign");
                                break;

                            case 0x27:
                                // Animation (serverbound)
                                await this.Logger.LogMessageAsync("Received animation (serverbound)");
                                break;

                            case 0x28:
                                // Spectate
                                await this.Logger.LogMessageAsync("Received spectate");
                                break;

                            case 0x29:
                                // Player Block Placement
                                await this.Logger.LogMessageAsync("Received player block placement");
                                break;

                            case 0x2A:
                                // Use Item
                                await this.Logger.LogMessageAsync("Received use item");
                                break;
                        }
                        break;
                }
            }

            await Logger.LogMessageAsync($"Disconnected client");
            if (this.Player != null)
                await this.OriginServer.SendChatAsync(string.Format(this.Config.LeaveMessage, this.Player.Username), this, 0, true);

            if (Tcp.Connected)
                this.Tcp.Close();
        }

        public void Disconnect() => this.Cancellation.Cancel();

        private async Task ConnectAsync(Guid uuid, Packet packet)
        {
            await this.Logger.LogMessageAsync($"Sent Login success to User {this.Player.Username} {this.Player.UUID.ToString()}");

            await Packet.CreateAsync(new LoginSuccess(uuid, this.Player.Username), this.MinecraftStream);

            this.State = PacketState.Play;

            await Packet.CreateAsync(new JoinGame((int)(EntityId.Player | (EntityId)this.PlayerId), 0, 0, 0, "default", true), this.MinecraftStream);
            await this.Logger.LogMessageAsync("Sent Join Game packet.");

            await Packet.CreateAsync(new SpawnPosition(new Location(0, 100, 0)), this.MinecraftStream);
            await this.Logger.LogMessageAsync("Sent Spawn Position packet.");

            await Packet.CreateAsync(new PlayerPositionLook(new Location(0, 100, 0), PositionFlags.NONE, 0), this.MinecraftStream);
            await this.Logger.LogMessageAsync("Sent Position packet.");

            await this.SendChatAsync("§dWelcome to Obsidian Test Build. §l§4<3", 2);

            await this.OriginServer.SendChatAsync(string.Format(this.Config.JoinMessage, this.Player.Username), this, system: true);
            await this.OriginServer.Events.InvokePlayerJoin(new PlayerJoinEventArgs(this, packet, DateTimeOffset.Now));

            await this.SendDeclareCommandsAsync();
            await this.SendPlayerInfoAsync();
        }
    }
}
