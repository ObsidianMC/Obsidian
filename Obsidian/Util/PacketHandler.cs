using Obsidian.Entities;
using Obsidian.Logging;
using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Obsidian.Util
{
    public class PacketHandler
    {
#if DEBUG
        private static readonly Logger Logger = new Logger("Packets", LogLevel.Debug);
#else
        private static Logger Logger = new Logger("Packets", LogLevel.Error);
#endif

        private ArrayPool<byte> _pool = ArrayPool<byte>.Shared;

        public static ProtocolVersion Protocol = ProtocolVersion.v1_13_2;

        public static async Task SerializeAsync(Packet packet, MinecraftStream outStream)
        {
            var properties = from p in packet.GetType().GetProperties()
                             let attrs = p.GetCustomAttributes(typeof(VariableAttribute), false)
                             where attrs.Length == 1
                             select new { Property = p, Attribute = attrs.First() as VariableAttribute };

            using (var stream = new MinecraftStream())
            {
                foreach (var prop in properties)
                {
                    var value = prop.Property.GetValue(packet);

                    switch (prop.Attribute.Type)
                    {
                        case VariableType.Int:
                            await stream.WriteIntAsync((int)value);
                            break;
                        case VariableType.Long:
                            await stream.WriteLongAsync((long)value);
                            break;
                        case VariableType.VarInt:
                            await stream.WriteVarIntAsync((int)value);
                            break;
                        case VariableType.VarLong:
                            await stream.WriteVarLongAsync((long)value);
                            break;
                        default:
                            await stream.WriteAutoAsync(value);
                            break;
                    }
                }

                await stream.CopyToAsync(outStream);
            }
        }

        public static async Task<T> DeserializeAsync<T>(T packet) where T : Packet
        {
            var properties = from p in packet.GetType().GetProperties()
                             let attrs = p.GetCustomAttributes(typeof(VariableAttribute), false)
                             where attrs.Length == 1
                             select new { Property = p, Attribute = attrs.First() as VariableAttribute };

            using (var stream = new MinecraftStream(packet.PacketData))
            {
                foreach (var prop in properties)
                {
                    var value = prop.Property.GetValue(packet);

                    switch (prop.Attribute.Type)
                    {
                        case VariableType.Int:
                            prop.Property.SetValue(packet, await stream.ReadIntAsync());
                            break;
                        case VariableType.Long:
                            prop.Property.SetValue(packet, await stream.ReadLongAsync());
                            break;
                        case VariableType.VarInt:
                            prop.Property.SetValue(packet, await stream.ReadVarIntAsync());
                            break;
                        case VariableType.VarLong:
                            prop.Property.SetValue(packet, await stream.ReadVarLongAsync());
                            break;
                        case VariableType.UnsignedByte:
                            prop.Property.SetValue(packet, await stream.ReadUnsignedByteAsync());
                            break;
                        case VariableType.Byte:
                            prop.Property.SetValue(packet, await stream.ReadByteAsync());
                            break;
                        case VariableType.Short:
                            prop.Property.SetValue(packet, await stream.ReadShortAsync());
                            break;
                        case VariableType.UnsignedShort:
                            prop.Property.SetValue(packet, await stream.ReadUnsignedShortAsync());
                            break;
                        case VariableType.String:
                            prop.Property.SetValue(packet, await stream.ReadStringAsync());
                            break;
                        case VariableType.Array:
                            prop.Property.SetValue(packet, await stream.ReadUInt8ArrayAsync(prop.Attribute.Size));
                            break;
                        case VariableType.List:
                            //TODO
                            break;
                        case VariableType.Position:
                            prop.Property.SetValue(packet, await stream.ReadPositionAsync());
                            break;
                        case VariableType.Boolean:
                            prop.Property.SetValue(packet, await stream.ReadBooleanAsync());
                            break;
                        case VariableType.Float:
                            prop.Property.SetValue(packet, await stream.ReadFloatAsync());
                            break;
                        case VariableType.Double:
                            prop.Property.SetValue(packet, await stream.ReadDoubleAsync());
                            break;
                        case VariableType.Tranform:
                            prop.Property.SetValue(packet, await stream.ReadTransformAsync());
                            break;
                    }
                }
            }

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

        public const float MaxDiggingRadius = 6;

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
                    Logger.LogDebug($"received chat: {message.Message}");

                    await server.ParseMessage(message.Message, client);
                    break;

                case 0x03:
                    Logger.LogDebug("Received client status");
                    break;

                case 0x04:
                    // Client Settings
                    client.ClientSettings = await DeserializeAsync(new ClientSettings(packet.PacketData));
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
                    var keepalive = await DeserializeAsync(new KeepAlive(packet.PacketData));

                    Logger.LogDebug($"Successfully kept alive player {client.Player.Username} with ka id {keepalive.KeepAliveId}");
                    break;

                case 0x0F: // Player
                    var onground = BitConverter.ToBoolean(await packet.SerializeAsync(), 0);
                    Logger.LogDebug($"{client.Player.Username} on ground?: {onground}");
                    client.Player.OnGround = onground;
                    break;

                case 0x10:// Player Position
                    var pos = await DeserializeAsync(new PlayerPosition(packet.PacketData));
                    client.Player.UpdatePosition(pos.Position, pos.OnGround);
                    //Logger.LogDebugAsync($"Updated position for {client.Player.Username}");
                    break;

                case 0x11: // Player Position And Look (serverbound)
                    var ppos = await DeserializeAsync(new PlayerPositionLook(packet.PacketData));

                    client.Player.UpdatePosition(ppos.Transform);
                    //Logger.LogDebugAsync($"Updated look and position for {this.Player.Username}");
                    break;

                case 0x12:
                    // Player Look
                    var look = await DeserializeAsync(new PlayerLook(packet.PacketData));

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

                    var digging = await DeserializeAsync(new PlayerDigging(packet.PacketData));

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
                    var serverAnim = await DeserializeAsync(new AnimationServerPacket(packet.PacketData));

                    Logger.LogDebug("Received animation (serverbound)");
                    break;

                case 0x28:
                    // Spectate
                    Logger.LogDebug("Received spectate");
                    break;

                case 0x29:
                    // Player Block Placement
                    var pbp = await DeserializeAsync(new PlayerBlockPlacement(packet.PacketData));

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