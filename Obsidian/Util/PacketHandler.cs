using Obsidian.Logging;
using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play;
using Obsidian.Serializer;
using Obsidian.Util.Extensions;
using SharpCompress.Compressors.Deflate;
using System;
using System.Threading.Tasks;

namespace Obsidian.Util
{
    public class PacketHandler
    {
        private static readonly AsyncLogger Logger = new AsyncLogger("Packets", LogLevel.Debug, "packets.log");

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

            return new Packet(packetId, packetData);
        }

        public static async Task<Packet> ReadCompressedPacketAsync(MinecraftStream stream)
        {
            var packetLength = await stream.ReadVarIntAsync();
            var dataLength = await stream.ReadVarIntAsync();

            using var deStream = new MinecraftStream(new ZlibStream(stream, SharpCompress.Compressors.CompressionMode.Decompress, CompressionLevel.BestSpeed));

            var packetId = await deStream.ReadVarIntAsync();
            var packetData = await deStream.ReadUInt8ArrayAsync(dataLength - packetId.GetVarIntLength());

            return new Packet(packetId, packetData);
        }


        public static async Task HandlePlayPackets(Packet packet, Client client)
        {
            Server server = client.Server;
            switch (packet.id)
            {
                case 0x00:
                    // Teleport Confirm
                    // GET X Y Z FROM PACKET TODO
                    //this.Player.Position = new Position((int)x, (int)y, (int)z);
                    //await Logger.LogDebugAsync("Received teleport confirm");
                    break;

                case 0x01:
                    // Query Block NBT
                    await Logger.LogDebugAsync("Received query block nbt");
                    break;

                case 0x02:
                    // Incoming chat message
                    var message = await PacketSerializer.DeserializeAsync<IncomingChatMessage>(packet.data);

                    await server.ParseMessage(message.Message, client);
                    break;

                case 0x03:
                    await Logger.LogDebugAsync("Received client status");
                    break;

                case 0x04:
                    // Client Settings
                    client.ClientSettings = await PacketSerializer.DeserializeAsync<ClientSettings>(packet.data);
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
                    var keepalive = await PacketSerializer.DeserializeAsync<KeepAlive>(packet.data);
                    await Logger.LogDebugAsync($"Successfully kept alive player {client.Player.Username} with ka id " +
                        $"{keepalive.KeepAliveId} previously missed {client.missedKeepalives - 1} ka's"); // missed is 1 more bc we just handled one
                    // Server is alive, reset missed keepalives.
                    client.missedKeepalives = 0;
                    break;

                case 0x0F: // Player
                    break;

                case 0x10:// Player Position
                    var pos = await PacketSerializer.DeserializeAsync<PlayerPosition>(packet.data);

                    client.Player.UpdatePosition(pos.Position, pos.OnGround);
                    //await Logger.LogDebugAsync($"Updated position for {client.Player.Username}");
                    break;

                case 0x11: // Player Position And Look (serverbound)
                    var ppos = await PacketSerializer.DeserializeAsync<PlayerPositionLook>(packet.data);

                    client.Player.UpdatePosition(ppos.Transform);
                    //await Logger.LogDebugAsyncAsync($"Updated look and position for {this.Player.Username}");
                    break;

                case 0x12:
                    // Player Look
                    var look = await PacketSerializer.DeserializeAsync<PlayerLook>(packet.data);

                    client.Player.UpdatePosition(look.Pitch, look.Yaw, look.OnGround);
                    //await Logger.LogDebugAsync($"Updated look for {client.Player.Username}");
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

                    var digging = await PacketSerializer.DeserializeAsync<PlayerDigging>(packet.data);

                    server.EnqueueDigging(digging);
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
                    var serverAnim = await PacketSerializer.DeserializeAsync<AnimationServerPacket>(packet.data);

                    await Logger.LogDebugAsync("Received animation (serverbound)");
                    break;

                case 0x28:
                    // Spectate
                    await Logger.LogDebugAsync("Received spectate");
                    break;

                case 0x29:
                    // Player Block Placement
                    var pbp = await PacketSerializer.DeserializeAsync<PlayerBlockPlacement>(packet.data);

                    await server.BroadcastBlockPlacementAsync(client.Player.Uuid, pbp);
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