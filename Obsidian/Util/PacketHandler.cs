using Obsidian.Entities;
using Obsidian.Logging;
using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play;
using System;
using System.Threading.Tasks;

namespace Obsidian.Util
{
    public class PacketHandler
    {
#if DEBUG
        private static Logger Logger = new Logger("Packets", LogLevel.Debug);
#else
        private static Logger Logger = new Logger("Packets", LogLevel.Error);
#endif

        private static ProtocolVersion Protocol => ServerStatus.DebugStatus.Version.Protocol;

        public static async Task<T> CreateAsync<T>(T packet, MinecraftStream stream = null) where T : Packet
        {
            if (packet.Empty)
            {
                await packet.FillPacketDataAsync();
            }
            else
            {
                await packet.PopulateAsync();
            }

            if (stream != null)
                await packet.WriteToStreamAsync(stream);

            return (T)Convert.ChangeType(packet, typeof(T));
        }

        public static async Task<Packet> ReadFromStreamAsync(MinecraftStream stream)
        {
            int length = await stream.ReadVarIntAsync();
            byte[] receivedData = new byte[length];

            await stream.ReadAsync(receivedData, 0, length);

            int packetId = 0;
            byte[] packetData = new byte[0];

            using (var packetStream = new MinecraftStream(receivedData))
            {
                try
                {
                    packetId = await packetStream.ReadVarIntAsync();
                    int arlen = 0;

                    if (length - packetId.GetVarintLength() > -1)
                        arlen = length - packetId.GetVarintLength();

                    packetData = new byte[arlen];
                    await packetStream.ReadAsync(packetData, 0, packetData.Length);
                }
                catch
                {
                    throw;
                }
            }

            return new EmptyPacket(packetId, packetData);
        }

        public static async Task HandlePlayPackets(Packet packet, Client client)
        {
            Server server = client.OriginServer;
            switch (packet.PacketId)
            {
                case 0x00:
                    // Teleport Confirm
                    // GET X Y Z FROM PACKET TODO
                    //this.Player.Position = new Position((int)x, (int)y, (int)z);
                    await Logger.LogDebugAsync("Received teleport confirm");
                    break;

                case 0x01:
                    // Query Block NBT
                    await Logger.LogDebugAsync("Received query block nbt");
                    break;

                case 0x02:
                    // Incoming chat message
                    var message = await CreateAsync(new IncomingChatMessage(packet.PacketData));
                    await Logger.LogDebugAsync($"received chat: {message.Message}");

                    await server.SendChatAsync(message.Message, client);
                    break;

                case 0x03:
                    await Logger.LogDebugAsync("Received client status");
                    break;

                case 0x04:
                    // Client Settings
                    client.ClientSettings = await CreateAsync(new ClientSettings(packet.PacketData));
                    await Logger.LogDebugAsync("Received client settings");
                    break;

                case 0x05:
                    // Tab-Complete
                    await Logger.LogDebugAsync("Received tab-complete");
                    break;

                case 0x06:
                    // Confirm Transaction
                    await Logger.LogDebugAsync("Received confirm transaction");
                    break;

                case 0x07:
                    // Enchant Item
                    await Logger.LogDebugAsync("Received enchant item");
                    break;

                case 0x08:
                    // Click Window
                    await Logger.LogDebugAsync("Received click window");
                    break;

                case 0x09:
                    // Close Window (serverbound)
                    await Logger.LogDebugAsync("Received close window");
                    break;

                case 0x0A:
                    // Plugin Message (serverbound)
                    await Logger.LogDebugAsync("Received plugin message");
                    break;

                case 0x0B:
                    // Edit Book
                    await Logger.LogDebugAsync("Received edit book");
                    break;

                case 0x0C:
                    // Query Entity NBT
                    await Logger.LogDebugAsync("Received query entity nbt");
                    break;

                case 0x0D:
                    // Use Entity
                    await Logger.LogDebugAsync("Received use entity");
                    break;

                case 0x0E:
                    // Keep Alive (serverbound)
                    var keepalive = await CreateAsync(new KeepAlive(packet.PacketData));

                    await Logger.LogDebugAsync($"Successfully kept alive player {client.Player.Username} with ka id {keepalive.KeepAliveId}");
                    break;

                case 0x0F: // Player
                    var onground = BitConverter.ToBoolean(await packet.ToArrayAsync(), 0);
                    await Logger.LogDebugAsync($"{client.Player.Username} on ground?: {onground}");
                    client.Player.OnGround = onground;
                    break;

                case 0x10:// Player Position
                    var pos = await CreateAsync(new PlayerPosition(packet.PacketData));
                    client.Player.UpdatePosition(pos.Position, pos.OnGround);
                    //await Logger.LogDebugAsync($"Updated position for {this.Player.Username}");
                    break;

                case 0x11: // Player Position And Look (serverbound)
                    var ppos = await CreateAsync(new PlayerPositionLook(packet.PacketData));

                    client.Player.UpdatePosition(ppos.Transform);
                    //await Logger.LogDebugAsync($"Updated look and position for {this.Player.Username}");
                    break;

                case 0x12:
                    // Player Look
                    var look = await CreateAsync(new PlayerLook(packet.PacketData));

                    client.Player.UpdatePosition(look.Pitch, look.Yaw, look.OnGround);
                    await Logger.LogDebugAsync($"Updated look for {client.Player.Username}");
                    break;

                case 0x13:
                    // Vehicle Move (serverbound)
                    await Logger.LogDebugAsync("Received vehicle move");
                    break;

                case 0x14:
                    // Steer Boat
                    await Logger.LogDebugAsync("Received steer boat");
                    break;

                case 0x15:
                    // Pick Item
                    await Logger.LogDebugAsync("Received pick item");
                    break;

                case 0x16:
                    // Craft Recipe Request
                    await Logger.LogDebugAsync("Received craft recipe request");
                    break;

                case 0x17:
                    // Player Abilities (serverbound)
                    await Logger.LogDebugAsync("Received player abilities");
                    break;

                case 0x18:
                    // Player Digging
                    await Logger.LogDebugAsync("Received player digging");
                    // owo, player be digging
                    var digging = new PlayerDigging(packet.PacketData);
                    await digging.PopulateAsync();
                    await Logger.LogMessageAsync("Populated player digging");

                    await Logger.LogMessageAsync("Enqueueuing player digging");
                    // enqueue for server to handle
                    await server.EnqueueDigging(digging);
                    await Logger.LogMessageAsync("ok thas done");
                    break;

                case 0x19:
                    // Entity Action
                    await Logger.LogDebugAsync("Received entity action");
                    break;

                case 0x1A:
                    // Steer Vehicle
                    await Logger.LogDebugAsync("Received steer vehicle");
                    break;

                case 0x1B:
                    // Recipe Book Data
                    await Logger.LogDebugAsync("Received recipe book data");
                    break;

                case 0x1C:
                    // Name Item
                    await Logger.LogDebugAsync("Received name item");
                    break;

                case 0x1D:
                    // Resource Pack Status
                    await Logger.LogDebugAsync("Received resource pack status");
                    break;

                case 0x1E:
                    // Advancement Tab
                    await Logger.LogDebugAsync("Received advancement tab");
                    break;

                case 0x1F:
                    // Select Trade
                    await Logger.LogDebugAsync("Received select trade");
                    break;

                case 0x20:
                    // Set Beacon Effect
                    await Logger.LogDebugAsync("Received set beacon effect");
                    break;

                case 0x21:
                    // Held Item Change (serverbound)
                    await Logger.LogDebugAsync("Received held item change");
                    break;

                case 0x22:
                    // Update Command Block
                    await Logger.LogDebugAsync("Received update command block");
                    break;

                case 0x23:
                    // Update Command Block Minecart
                    await Logger.LogDebugAsync("Received update command block minecart");
                    break;

                case 0x24:
                    // Creative Inventory Action
                    await Logger.LogDebugAsync("Received creative inventory action");
                    break;

                case 0x25:
                    // Update Structure Block
                    await Logger.LogDebugAsync("Received update structure block");
                    break;

                case 0x26:
                    // Update Sign
                    await Logger.LogDebugAsync("Received update sign");
                    break;

                case 0x27:
                    // Animation (serverbound)
                    var serverAnim = await CreateAsync(new AnimationServerPacket(packet.PacketData));

                    await Logger.LogDebugAsync("Received animation (serverbound)");
                    break;

                case 0x28:
                    // Spectate
                    await Logger.LogDebugAsync("Received spectate");
                    break;

                case 0x29:
                    // Player Block Placement
                    var pbp = await CreateAsync(new PlayerBlockPlacement(packet.PacketData));
                    await server.EnqueuePlacing(pbp);
                    await Logger.LogDebugAsync("Received player block placement");
                    break;

                case 0x2A:
                    // Use Item
                    await Logger.LogDebugAsync("Received use item");
                    break;
            }
        }
    }
}