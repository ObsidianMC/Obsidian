using fNbt;
using Newtonsoft.Json;
using Obsidian.Chat;
using Obsidian.Util;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Obsidian.Util.DataTypes;

namespace Obsidian.Net
{
    public partial class MinecraftStream
    {
        static MinecraftStream()
        {
            StringEncoding = Encoding.UTF8;
        }

        public static Encoding StringEncoding { get; }

        public Semaphore semaphore;

        #region Writing

        public async Task WriteAngleAsync(Angle angle)
        {
            await this.WriteUnsignedByteAsync(angle.Value);
        }

        public async Task WriteEntityMetdata(byte index, EntityMetadataType type, object value, bool optional = false)
        {
            await this.WriteUnsignedByteAsync(index);
            await this.WriteVarIntAsync((int)type);
            switch (type)
            {
                case EntityMetadataType.Byte:
                    await this.WriteUnsignedByteAsync((byte)value);
                    break;

                case EntityMetadataType.VarInt:
                    await this.WriteVarIntAsync((int)value);
                    break;

                case EntityMetadataType.Float:
                    await this.WriteFloatAsync((float)value);
                    break;

                case EntityMetadataType.String:
                    await this.WriteStringAsync((string)value, 3276);
                    break;

                case EntityMetadataType.Chat:
                    await this.WriteChatAsync((ChatMessage)value);
                    break;

                case EntityMetadataType.OptChat:
                    await this.WriteBooleanAsync(optional);
                    await this.WriteChatAsync((ChatMessage)value);
                    break;

                case EntityMetadataType.Slot:
                    await this.WriteUnsignedByteAsync((byte)value);
                    break;

                case EntityMetadataType.Boolean:
                    await this.WriteBooleanAsync((bool)value);
                    break;

                case EntityMetadataType.Rotation:
                    break;

                case EntityMetadataType.Position:
                    await this.WritePositionAsync((Position)value);
                    break;

                case EntityMetadataType.OptPosition:
                    await this.WriteBooleanAsync(optional);
                    await this.WritePositionAsync((Position)value);
                    break;

                case EntityMetadataType.Direction:
                    break;

                case EntityMetadataType.OptUuid:
                    await this.WriteBooleanAsync(optional);
                    await this.WriteUuidAsync((Guid)value);
                    break;

                case EntityMetadataType.OptBlockId:
                    await this.WriteVarIntAsync((int)value);
                    break;

                case EntityMetadataType.Nbt:
                    break;

                case EntityMetadataType.Particle:
                    break;

                default:
                    break;
            }
        }

        public async Task WriteByteAsync(sbyte value)
        {
#if PACKETLOG
            Program.PacketLogger.LogDebug($"Writing Byte (0x{value.ToString("X")})");
#endif

            await this.WriteUnsignedByteAsync((byte)value);
        }

        public async Task WriteUnsignedByteAsync(byte value)
        {
#if PACKETLOG
            Program.PacketLogger.LogDebug($"Writing unsigned Byte (0x{value.ToString("X")})");
#endif

            await this.WriteAsync(new[] { value });
        }

        public async Task WriteBooleanAsync(bool value)
        {
#if PACKETLOG
            Program.PacketLogger.LogDebug($"Writing Boolean ({value})");
#endif

            await this.WriteByteAsync((sbyte)(value ? 0x01 : 0x00));
        }

        public async Task WriteUnsignedShortAsync(ushort value)
        {
#if PACKETLOG
            Program.PacketLogger.LogDebug($"Writing unsigned Short ({value})");
#endif

            var write = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(write);
            }
            await this.WriteAsync(write);
        }

        public async Task WriteShortAsync(short value)
        {
#if PACKETLOG
            Program.PacketLogger.LogDebug($"Writing Short ({value})");
#endif

            var write = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(write);
            }
            await this.WriteAsync(write);
        }

        public async Task WriteIntAsync(int value)
        {
#if PACKETLOG
            Program.PacketLogger.LogDebug($"Writing Int ({value})");
#endif

            var write = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(write);
            }
            await this.WriteAsync(write);
        }

        public async Task WriteLongAsync(long value)
        {
#if PACKETLOG
            Program.PacketLogger.LogDebug($"Writing Long ({value})");
#endif

            var write = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(write);
            }
            await this.WriteAsync(write);
        }

        public async Task WriteFloatAsync(float value)
        {
#if PACKETLOG
            Program.PacketLogger.LogDebug($"Writing Float ({value})");
#endif

            var write = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(write);
            }
            await this.WriteAsync(write);
        }

        public async Task WriteDoubleAsync(double value)
        {
#if PACKETLOG
            Program.PacketLogger.LogDebug($"Writing Double ({value})");
#endif

            var write = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(write);
            }
            await this.WriteAsync(write);
        }

        public async Task WriteStringAsync(string value, int maxLength = 0)
        {
#if PACKETLOG
            Program.PacketLogger.LogDebug($"Writing String ({value})");
#endif

            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (maxLength > 0 && value.Length > maxLength)
            {
                throw new ArgumentException($"string ({value.Length}) exceeded maximum length ({maxLength})", nameof(value));
            }
            var bytes = Encoding.UTF8.GetBytes(value);
            await this.WriteVarIntAsync(bytes.Length);
            await this.WriteAsync(bytes);
        }

        public async Task WriteUuidAsync(Guid value) => await this.WriteAsync(value.ToByteArray());

        public async Task WriteChatAsync(ChatMessage value) => await this.WriteStringAsync(value.ToString(), 32767);

        public async Task WriteIdentifierAsync(string value) => await this.WriteStringAsync(value, 32767);

        public async Task WriteVarIntAsync(int value)
        {
#if PACKETLOG
            Program.PacketLogger.LogDebug($"Writing VarInt ({value})");
#endif

            uint v = (uint)value;

            do
            {
                byte temp = (byte)(v & 127);

                v >>= 7;

                if (v != 0)
                {
                    temp |= 128;
                }

                await this.WriteUnsignedByteAsync(temp);
            } while (v != 0);
        }

        /// <summary>
        /// Writes a "VarInt Enum" to the specified <paramref name="stream"/>.
        /// </summary>
        public async Task WriteVarIntAsync(Enum value) => await this.WriteVarIntAsync(Convert.ToInt32(value));

        public async Task WriteAutoAsync(params object[] values)
        {
            foreach (object value in values)
            {
                switch (value)
                {
                    case int intValue: await this.WriteVarIntAsync(intValue); break;
                    case string stringValue: await this.WriteStringAsync(stringValue); break;
                    case float floatValue: await this.WriteFloatAsync(floatValue); break;
                    case double doubleValue: await this.WriteDoubleAsync(doubleValue); break;
                    case short shortValue: await this.WriteShortAsync(shortValue); break;
                    case ushort ushortValue: await this.WriteUnsignedShortAsync(ushortValue); break;
                    case long longValue: await this.WriteVarLongAsync(longValue); break;
                    case bool boolValue: await this.WriteBooleanAsync(boolValue); break;
                    case Enum enumValue: await this.WriteVarIntAsync(enumValue); break;
                    case ChatMessage chatValue: await this.WriteChatAsync(chatValue); break;
                    case Guid uuidValue: await this.WriteUuidAsync(uuidValue); break;
                    case byte[] byteArray: await this.WriteAsync(byteArray); break;
                    case object[] objectArray: await this.WriteAutoAsync(objectArray); break;
                    case sbyte sbyteValue: await this.WriteByteAsync(sbyteValue); break;
                    case byte byteValue: await this.WriteUnsignedByteAsync(byteValue); break;
                    default: throw new Exception($"Can't handle {value.ToString()} ({value.GetType().ToString()})");
                }
            }
        }

        public async Task WriteLongArrayAsync(long[] value)
        {
            for (var i = 0; i < value.Length; i++)
                await this.WriteLongAsync(value[i]);
        }

        public async Task WriteLongArrayAsync(ulong[] value)
        {
            for (var i = 0; i < value.Length; i++)
                await this.WriteLongAsync((long)value[i]);
        }

        public async Task WriteVarLongAsync(long value)
        {
#if PACKETLOG
            Program.PacketLogger.LogDebug($"Writing VarLong ({value})");
#endif

            ulong v = (ulong)value;

            do
            {
                byte temp = (byte)(v & 127);

                v >>= 7;

                if (v != 0)
                {
                    temp |= 128;
                }

                await this.WriteUnsignedByteAsync(temp);
            } while (v != 0);
        }

        public async Task WritePositionAsync(Position value)
        {
            //this is 1.13
            long pos = (((long)value.X & 0x3FFFFFF) << 38) | (((long)value.Y & 0xFFF) << 26) | ((long)value.Z & 0x3FFFFFF);

            await this.WriteLongAsync(pos);
            //await this.WriteLongAsync((((value.X & 0x3FFFFFF) << 38) | ((value.Y & 0xFFF) << 26) | (value.Z & 0x3FFFFFF)));
        }

        public async Task WriteNbtAsync(NbtTag tag) => await this.WriteAsync(tag.ByteArrayValue);

        #endregion Writing

        #region Reading

        public async Task<sbyte> ReadByteAsync() => (sbyte)await this.ReadUnsignedByteAsync();

        public async Task<byte> ReadUnsignedByteAsync()
        {
            var buffer = new byte[1];
            await this.ReadAsync(buffer);
            return buffer[0];
        }

        public async Task<bool> ReadBooleanAsync()
        {
            var value = (int)await this.ReadByteAsync();
            if (value == 0x00)
            {
                return false;
            }
            else if (value == 0x01)
            {
                return true;
            }
            else
            {
                throw new ArgumentOutOfRangeException("Byte returned by stream is out of range (0x00 or 0x01)", nameof(BaseStream));
            }
        }

        public async Task<ushort> ReadUnsignedShortAsync()
        {
            var buffer = new byte[2];
            await this.ReadAsync(buffer);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            return BitConverter.ToUInt16(buffer);
        }

        public async Task<short> ReadShortAsync()
        {
            var buffer = new byte[2];
            await this.ReadAsync(buffer);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            return BitConverter.ToInt16(buffer);
        }

        public async Task<int> ReadIntAsync()
        {
            var buffer = new byte[4];
            await this.ReadAsync(buffer);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            return BitConverter.ToInt32(buffer);
        }

        public async Task<long> ReadLongAsync()
        {
            var buffer = new byte[8];
            await this.ReadAsync(buffer);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            return BitConverter.ToInt64(buffer);
        }

        public async Task<ulong> ReadUnsignedLongAsync()
        {
            var buffer = new byte[8];
            await this.ReadAsync(buffer);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            return BitConverter.ToUInt64(buffer);
        }

        public async Task<float> ReadFloatAsync()
        {
            var buffer = new byte[4];
            await this.ReadAsync(buffer);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            return BitConverter.ToSingle(buffer);
        }

        public async Task<double> ReadDoubleAsync()
        {
            var buffer = new byte[8];
            await this.ReadAsync(buffer);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            return BitConverter.ToDouble(buffer);
        }

        public async Task<string> ReadStringAsync(int maxLength = 0)
        {
            var length = await this.ReadVarIntAsync();
            var buffer = new byte[length];
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            await this.ReadAsync(buffer, 0, length);

            var value = Encoding.UTF8.GetString(buffer);
            if (maxLength > 0 && value.Length > maxLength)
            {
                throw new ArgumentException($"string ({value.Length}) exceeded maximum length ({maxLength})", nameof(value));
            }
            return value;
        }

        public async Task<ChatMessage> ReadChatAsync()
        {
            var chat = await this.ReadStringAsync();

            if (chat.Length > 32767)
            {
                throw new ArgumentException("string provided by stream exceeded maximum length", nameof(BaseStream));
            }

            return JsonConvert.DeserializeObject<ChatMessage>(chat);
        }

        public async Task<string> ReadIdentifierAsync()
        {
            var identifier = await this.ReadStringAsync();
            if (identifier.Length > 32767) throw new ArgumentException("string provided by stream exceeded maximum length", nameof(BaseStream));
            return identifier;
        }

        public virtual async Task<int> ReadVarIntAsync()
        {
            int numRead = 0;
            int result = 0;
            byte read;
            do
            {
                read = await this.ReadUnsignedByteAsync();
                int value = read & 0b01111111;
                result |= value << (7 * numRead);

                numRead++;
                if (numRead > 5)
                {
                    throw new InvalidOperationException("VarInt is too big");
                }
            } while ((read & 0b10000000) != 0);

            return result;
        }

        public async Task<byte[]> ReadUInt8ArrayAsync(int length)
        {
            var result = new byte[length];
            if (length == 0) return result;
            int n = length;
            while (true)
            {
                n -= await this.ReadAsync(result, length - n, n);
                if (n == 0)
                    break;
                await Task.Delay(1);
            }
            return result;
        }

        public async Task<byte> ReadUInt8Async()
        {
            int value = await this.ReadByteAsync();
            if (value == -1)
                throw new EndOfStreamException();
            return (byte)value;
        }

        public async Task<long> ReadVarLongAsync()
        {
            int numRead = 0;
            long result = 0;
            byte read;
            do
            {
                read = await this.ReadUnsignedByteAsync();
                int value = (read & 0b01111111);
                result |= (long)value << (7 * numRead);

                numRead++;
                if (numRead > 10)
                {
                    throw new InvalidOperationException("VarLong is too big");
                }
            } while ((read & 0b10000000) != 0);

            return result;
        }

        public async Task<Position> ReadPositionAsync()
        {
            ulong value = await this.ReadUnsignedLongAsync();
            int x = (int)(value >> 38), y = (int)((value >> 26) & 0xFFF), z = (int)(value << 38 >> 38);

            if (PacketHandler.Protocol == ProtocolVersion.v1_14)
            {
                x = (int)(value >> 38);
                y = (int)value & 0xFFF;
                z = (int)(value << 26 >> 38);
            }

            if (x >= Math.Pow(2, 25)) { x -= (int)Math.Pow(2, 26); }
            if (y >= Math.Pow(2, 11)) { y -= (int)Math.Pow(2, 12); }
            if (z >= Math.Pow(2, 25)) { z -= (int)Math.Pow(2, 26); }

            return new Position
            {
                X = x,

                Y = y,

                Z = z,
            };
        }

        #endregion Reading
    }

    public enum EntityMetadataType : int
    {
        Byte,

        VarInt,

        Float,

        String,

        Chat,

        OptChat,

        Slot,

        Boolean,

        Rotation,

        Position,

        OptPosition,

        Direction,

        OptUuid,

        OptBlockId,

        Nbt,

        Particle
    }
}