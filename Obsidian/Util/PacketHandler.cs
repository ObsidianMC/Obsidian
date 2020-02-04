using Obsidian.Logging;
using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play;
using SharpCompress.Compressors.Deflate;
using System;
using System.IO.Compression;
using System.Threading.Tasks;

namespace Obsidian.Util
{
    public class PacketHandler
    {
#if DEBUG
        private static readonly AsyncLogger Logger = new AsyncLogger("Packets", LogLevel.Debug);
#else
        private static Logger Logger = new Logger("Packets", LogLevel.Error);
#endif

        public static ProtocolVersion Protocol = ProtocolVersion.v1_13_2;

        public const float MaxDiggingRadius = 6;

        public static async Task<Packet> ReadPacketAsync(MinecraftStream stream)
        {
            int length = await stream.ReadVarIntAsync();
            byte[] receivedData = new byte[length];

            await stream.ReadAsync(receivedData, 0, length);

            int packetId = 0;
            byte[] packetData = Array.Empty<byte>();

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

            return new Packet(packetId, packetData);
        }

        public static async Task<Packet> ReadCompressedPacketAsync(MinecraftStream stream)
        {
            var packetLength = await stream.ReadVarIntAsync();
            var dataLength = await stream.ReadVarIntAsync();

            using var deStream = new MinecraftStream(new ZlibStream(stream, SharpCompress.Compressors.CompressionMode.Decompress, SharpCompress.Compressors.Deflate.CompressionLevel.BestSpeed));

            var packetId = await deStream.ReadVarIntAsync();
            var packetData = await deStream.ReadUInt8ArrayAsync(dataLength - packetId.GetVarintLength());

            return new Packet(packetId, packetData);
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
                    //Logger.LogDebug("Received teleport confirm");
                    break;

                case 0x01:
                    // Query Block NBT
                    Logger.LogDebug("Received query block nbt");
                    break;

                case 0x02:
                    // Incoming chat message
                    var message = new IncomingChatMessage(packet.PacketData);
                    await message.ReadAsync(packet.PacketData);
                    Logger.LogDebug($"received chat: {message.Message}");

                    await server.ParseMessage(message.Message, client);
                    break;

                case 0x03:
                    Logger.LogDebug("Received client status");
                    break;

                case 0x04:
                    // Client Settings
                    client.ClientSettings = new ClientSettings(packet.PacketData);
                    await client.ClientSettings.ReadAsync(packet.PacketData);
                    Logger.LogDebug("Received client settings");
                    break;

                case 0x05:
                    // Tab-Complete
                    Logger.LogDebug("Received tab-complete");
                    break;

                case 0x06:
                    // Confirm Transaction
                    Logger.LogDebug("Received confirm transaction");
                    break;

                case 0x07:
                    // Enchant Item
                    Logger.LogDebug("Received enchant item");
                    break;

                case 0x08:
                    // Click Window
                    Logger.LogDebug("Received click window");
                    break;

                case 0x09:
                    // Close Window (serverbound)
                    Logger.LogDebug("Received close window");
                    break;

                case 0x0A:
                    // Plugin Message (serverbound)
                    Logger.LogDebug("Received plugin message");
                    break;

                case 0x0B:
                    // Edit Book
                    Logger.LogDebug("Received edit book");
                    break;

                case 0x0C:
                    // Query Entity NBT
                    Logger.LogDebug("Received query entity nbt");
                    break;

                case 0x0D:
                    // Use Entity
                    Logger.LogDebug("Received use entity");
                    break;

                case 0x0E:
                    // Keep Alive (serverbound)
                    var keepalive = new KeepAlive(packet.PacketData);
                await keepalive.ReadAsync(packet.PacketData);
                    Logger.LogDebug($"Successfully kept alive player {client.Player.Username} with ka id " +
                        $"{keepalive.KeepAliveId} previously missed {client.MissedKeepalives - 1} ka's"); // missed is 1 more bc we just handled one
                    // Server is alive, reset missed keepalives.
                    client.MissedKeepalives = 0;
                    break;

                case 0x0F: // Player
                    //TODO: Please rewrite.
                    //var onground = BitConverter.ToBoolean(await packet.ToArrayAsync(), 0);
                    //Logger.LogDebug($"{client.Player.Username} on ground?: {onground}");
                    //client.Player.OnGround = onground;
                    break;

                case 0x10:// Player Position
                    var pos = new PlayerPosition(packet.PacketData);
                    await pos.ReadAsync(packet.PacketData);
                    client.Player.UpdatePosition(pos.Position, pos.OnGround);
                    Logger.LogDebug($"Updated position for {client.Player.Username}");
                    break;

                case 0x11: // Player Position And Look (serverbound)
                    var ppos = new PlayerPositionLook(packet.PacketData);
                    await ppos.ReadAsync(packet.PacketData);

                    client.Player.UpdatePosition(ppos.Transform);
                    //Logger.LogDebugAsync($"Updated look and position for {this.Player.Username}");
                    break;

                case 0x12:
                    // Player Look
                    var look = new PlayerLook(packet.PacketData);
                    await look.ReadAsync(packet.PacketData);

                    client.Player.UpdatePosition(look.Pitch, look.Yaw, look.OnGround);
                    Logger.LogDebug($"Updated look for {client.Player.Username}");
                    break;

                case 0x13:
                    // Vehicle Move (serverbound)
                    Logger.LogDebug("Received vehicle move");
                    break;

                case 0x14:
                    // Steer Boat
                    Logger.LogDebug("Received steer boat");
                    break;

                case 0x15:
                    // Pick Item
                    Logger.LogDebug("Received pick item");
                    break;

                case 0x16:
                    // Craft Recipe Request
                    Logger.LogDebug("Received craft recipe request");
                    break;

                case 0x17:
                    // Player Abilities (serverbound)
                    Logger.LogDebug("Received player abilities");
                    break;

                case 0x18:
                    // Player Digging
                    Logger.LogDebug("Received player digging");

                    var digging = new PlayerDigging(packet.PacketData);
                    await digging.ReadAsync(packet.PacketData);

                    server.EnqueueDigging(digging);
                    break;

                case 0x19:
                    // Entity Action
                    Logger.LogDebug("Received entity action");
                    break;

                case 0x1A:
                    // Steer Vehicle
                    Logger.LogDebug("Received steer vehicle");
                    break;

                case 0x1B:
                    // Recipe Book Data
                    Logger.LogDebug("Received recipe book data");
                    break;

                case 0x1C:
                    // Name Item
                    Logger.LogDebug("Received name item");
                    break;

                case 0x1D:
                    // Resource Pack Status
                    Logger.LogDebug("Received resource pack status");
                    break;

                case 0x1E:
                    // Advancement Tab
                    Logger.LogDebug("Received advancement tab");
                    break;

                case 0x1F:
                    // Select Trade
                    Logger.LogDebug("Received select trade");
                    break;

                case 0x20:
                    // Set Beacon Effect
                    Logger.LogDebug("Received set beacon effect");
                    break;

                case 0x21:
                    // Held Item Change (serverbound)
                    Logger.LogDebug("Received held item change");
                    break;

                case 0x22:
                    // Update Command Block
                    Logger.LogDebug("Received update command block");
                    break;

                case 0x23:
                    // Update Command Block Minecart
                    Logger.LogDebug("Received update command block minecart");
                    break;

                case 0x24:
                    // Creative Inventory Action
                    Logger.LogDebug("Received creative inventory action");
                    break;

                case 0x25:
                    // Update Structure Block
                    Logger.LogDebug("Received update structure block");
                    break;

                case 0x26:
                    // Update Sign
                    Logger.LogDebug("Received update sign");
                    break;

                case 0x27:
                    // Animation (serverbound)
                    var serverAnim = new AnimationServerPacket(packet.PacketData);
                    await serverAnim.ReadAsync(packet.PacketData);

                    Logger.LogDebug("Received animation (serverbound)");
                    break;

                case 0x28:
                    // Spectate
                    Logger.LogDebug("Received spectate");
                    break;

                case 0x29:
                    // Player Block Placement
                    var pbp = new PlayerBlockPlacement(packet.PacketData);

                    await pbp.ReadAsync(packet.PacketData);

                    server.EnqueuePlacing(pbp);
                    Logger.LogDebug("Received player block placement");

                    break;

                case 0x2A:
                    // Use Item
                    Logger.LogDebug("Received use item");
                    break;
            }
        }
    }
}