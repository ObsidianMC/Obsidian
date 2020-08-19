
using fNbt;

using Newtonsoft.Json;
using Obsidian.Boss;
using Obsidian.Chat;
using Obsidian.Commands;
using Obsidian.GameState;
using Obsidian.Net.Packets.Play;
using Obsidian.PlayerData;
using Obsidian.PlayerData.Info;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using Obsidian.Util;
using Obsidian.Util.DataTypes;
using Obsidian.Util.Extensions;
using Obsidian.World;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Obsidian.Net
{
    public partial class MinecraftStream
    {
        public static Encoding StringEncoding { get; } = Encoding.UTF8;

        public readonly SemaphoreSlim Lock = new SemaphoreSlim(1, 1);

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
            await Program.PacketLogger.LogDebugAsync($"Writing Byte (0x{value.ToString("X")})");
#endif

            await this.WriteUnsignedByteAsync((byte)value);
        }

        public async Task WriteUnsignedByteAsync(byte value)
        {
#if PACKETLOG
            await Program.PacketLogger.LogDebugAsync($"Writing unsigned Byte (0x{value.ToString("X")})");
#endif

            await this.WriteAsync(new[] { value });
        }

        public async Task WriteBooleanAsync(bool value)
        {
#if PACKETLOG
            await Program.PacketLogger.LogDebugAsync($"Writing Boolean ({value})");
#endif

            await this.WriteByteAsync((sbyte)(value ? 0x01 : 0x00));
        }

        public async Task WriteUnsignedShortAsync(ushort value)
        {
#if PACKETLOG
            await Program.PacketLogger.LogDebugAsync($"Writing unsigned Short ({value})");
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
            await Program.PacketLogger.LogDebugAsync($"Writing Short ({value})");
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
            await Program.PacketLogger.LogDebugAsync($"Writing Int ({value})");
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
            await Program.PacketLogger.LogDebugAsync($"Writing Long ({value})");
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
            await Program.PacketLogger.LogDebugAsync($"Writing Float ({value})");
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
            await Program.PacketLogger.LogDebugAsync($"Writing Double ({value})");
#endif

            var write = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(write);
            }
            await this.WriteAsync(write);
        }

        public async Task WriteStringAsync(string value, int maxLength = 32767)
        {
            //await Program.PacketLogger.LogDebugAsync($"Writing String ({value})");

            if (value == null)
                throw new ArgumentNullException(nameof(value));

            if (value.Length > maxLength)
                throw new ArgumentException($"string ({value.Length}) exceeded maximum length ({maxLength})", nameof(value));

            var bytes = Encoding.UTF8.GetBytes(value);
            await this.WriteVarIntAsync(bytes.Length);
            await this.WriteAsync(bytes);
        }

        public async Task WriteUuidAsync(Guid value) => await this.WriteAsync(value.ToByteArray());

        public async Task WriteChatAsync(ChatMessage value) => await this.WriteStringAsync(value.ToString());

        public async Task WriteVarIntAsync(int value)
        {
            //await Program.PacketLogger.LogDebugAsync($"Writing VarInt ({value})");

            var v = (uint)value;

            do
            {
                var temp = (byte)(v & 127);

                v >>= 7;

                if (v != 0)
                    temp |= 128;

                await this.WriteUnsignedByteAsync(temp);
            } while (v != 0);
        }


        /// <summary>
        /// Writes a "VarInt Enum" to the specified <paramref name="stream"/>.
        /// </summary>
        public async Task WriteVarIntAsync(Enum value) => await this.WriteVarIntAsync(Convert.ToInt32(value));

        public async Task<object> ReadAsync(Type type, DataType dataType, FieldAttribute attr)
        {
            switch (dataType)
            {
                case DataType.Auto:
                    return await this.ReadAutoAsync(type, attr.Absolute, attr.CountLength);
                case DataType.Boolean:
                    return await this.ReadBooleanAsync();
                case DataType.Byte:
                    return await this.ReadByteAsync();
                case DataType.UnsignedByte:
                    return await this.ReadUnsignedByteAsync();
                case DataType.Short:
                    return await this.ReadShortAsync();
                case DataType.UnsignedShort:
                    return await this.ReadUnsignedShortAsync();
                case DataType.Int:
                    return await this.ReadIntAsync();
                case DataType.Long:
                    return await this.ReadLongAsync();
                case DataType.Float:
                    return await this.ReadFloatAsync();
                case DataType.Double:
                    return await this.ReadDoubleAsync();
                case DataType.String:
                    return await this.ReadStringAsync(attr.MaxLength);
                case DataType.Chat:
                    return await this.ReadChatAsync();
                case DataType.Identifier:
                    return await this.ReadStringAsync();
                case DataType.VarInt:
                    return await this.ReadVarIntAsync();
                case DataType.VarLong:
                    return await this.ReadVarLongAsync();
                case DataType.Position:
                    {
                        if (type == typeof(Position))
                        {
                            if (attr.Absolute)
                                return new Position(await this.ReadDoubleAsync(), await this.ReadDoubleAsync(), await this.ReadDoubleAsync());

                            return await this.ReadPositionAsync();
                        }
                        else if (type == typeof(Transform))
                            return await this.ReadTransformAsync();
                        else if (type == typeof(SoundPosition))
                            return new SoundPosition(await this.ReadIntAsync(), await this.ReadIntAsync(), await this.ReadIntAsync());


                        return null;
                    }
                case DataType.Angle:
                    return this.ReadFloatAsync();
                case DataType.UUID:
                    return Guid.Parse(await this.ReadStringAsync());
                case DataType.Velocity:
                    return new Velocity(await this.ReadShortAsync(), await this.ReadShortAsync(), await this.ReadShortAsync());
                case DataType.EntityMetadata:
                case DataType.Slot:
                case DataType.NbtTag:
                case DataType.Array:
                default:
                    throw new NotImplementedException(nameof(type));
            }
        }

        private async Task<Transform> ReadTransformAsync()
        {
            var x = await this.ReadDoubleAsync();
            var y = await this.ReadDoubleAsync();
            var z = await this.ReadDoubleAsync();
            var pitch = await this.ReadFloatAsync();
            var yaw = await this.ReadFloatAsync();

            return new Transform(x, y, z, pitch, yaw);
        }

        [Obsolete("Shouldn't be used anymore")]
        public async Task<object> ReadAutoAsync(Type type, bool absolute = false, bool countLength = false)
        {
            if (type == typeof(int))
            {
                if (absolute)
                    return await this.ReadIntAsync();

                return await this.ReadVarIntAsync();
            }
            else if (type == typeof(string))
            {
                return await this.ReadStringAsync(32767) ?? string.Empty;
            }
            else if (type == typeof(float))
            {
                return await this.ReadFloatAsync();
            }
            else if (type == typeof(double))
            {
                return await this.ReadDoubleAsync();
            }
            else if (type == typeof(short))
            {
                return await this.ReadShortAsync();
            }
            else if (type == typeof(ushort))
            {
                return await this.ReadUnsignedShortAsync();
            }
            else if (type == typeof(long))
            {
                return await this.ReadLongAsync();
            }
            else if (type == typeof(bool))
            {
                return await this.ReadBooleanAsync();
            }
            else if (type == typeof(byte))
            {
                return await this.ReadUnsignedByteAsync();
            }
            else if (type == typeof(sbyte))
            {
                return await this.ReadByteAsync();
            }
            else if (type == typeof(byte[]) && countLength)
            {
                var length = await this.ReadVarIntAsync();
                return await this.ReadUInt8ArrayAsync(length);
            }
            else if (type == typeof(ChatMessage))
            {
                return JsonConvert.DeserializeObject<ChatMessage>(await this.ReadStringAsync());
            }
            else if (type == typeof(Position))
            {
                if (absolute)
                {
                    return new Position(await this.ReadDoubleAsync(), await this.ReadDoubleAsync(), await this.ReadDoubleAsync());
                }

                return await this.ReadPositionAsync();
            }
            else if (type == typeof(Transform))
            {
                return new Transform(await this.ReadDoubleAsync(), await this.ReadDoubleAsync(), await this.ReadDoubleAsync(), await this.ReadFloatAsync(), await this.ReadFloatAsync());
            }
            else if (type.BaseType != null && type.BaseType == typeof(Enum))
            {
                return await this.ReadVarIntAsync();
            }
            else
            {
                throw new InvalidOperationException($"Tried to read un-supported type {type}");
            }
        }

        [Obsolete("Shouldn't be used anymore")]
        public async Task WriteAutoAsync(object value, bool countLength = false)
        {
            //isn't there a better way to do this?
            switch (value)
            {
                case Enum enumValue:
                    if (enumValue is PositionFlags flags)
                    {
                        await this.WriteByteAsync((sbyte)flags);
                        break;
                    }

                    await this.WriteVarIntAsync(enumValue);
                    break;

                case Transform transform:
                    await this.WriteDoubleAsync(transform.X);
                    await this.WriteDoubleAsync(transform.Y);
                    await this.WriteDoubleAsync(transform.Z);
                    await this.WriteFloatAsync(transform.Yaw.Degrees);
                    await this.WriteFloatAsync(transform.Pitch.Degrees);
                    break;


                case Velocity velocity:
                    await this.WriteShortAsync(velocity.X);
                    await this.WriteShortAsync(velocity.Y);
                    await this.WriteShortAsync(velocity.Z);
                    break;

                case SoundPosition soundPos:
                    await this.WriteIntAsync(soundPos.X);
                    await this.WriteIntAsync(soundPos.Y);
                    await this.WriteIntAsync(soundPos.Z);
                    break;

                case BossBarAction actionValue:
                    await this.WriteVarIntAsync(actionValue.Action);
                    await this.WriteAsync(await actionValue.ToArrayAsync());
                    break;

                case ChangeGameStateReason gameStateValue:
                    await this.WriteUnsignedByteAsync(gameStateValue.Reason);
                    await this.WriteFloatAsync(gameStateValue.Value);
                    break;

                case List<CommandNode> nodes:
                    await this.WriteVarIntAsync(nodes.Count);
                    foreach (var node in nodes)
                        await node.CopyToAsync(this);

                    await this.WriteVarIntAsync(0);
                    break;

                case List<PlayerInfoAction> actions:
                    await this.WriteVarIntAsync(actions.Count);

                    foreach (var action in actions)
                        await action.WriteAsync(this);

                    break;

                case byte[] byteArray:
                    if (countLength)
                    {
                        await this.WriteVarIntAsync(byteArray.Length);
                        await this.WriteAsync(byteArray);
                    }
                    else
                    {
                        await this.WriteAsync(byteArray);
                    }
                    break;

                default:
                    throw new InvalidOperationException($"Can't handle {value} ({value.GetType()})");
            }
        }

        public async Task WriteAsync(DataType type, FieldAttribute attribute, object value, int length = 32767)
        {
            switch (type)
            {
                case DataType.Auto:
                    {
                        if (value is ChangeGameStateReason changeGameState)
                        {
                            await this.WriteUnsignedByteAsync(changeGameState.Reason);
                            await this.WriteFloatAsync(changeGameState.Value);
                        }
                        else
                            await this.WriteAutoAsync(value);

                        break;
                    }
                case DataType.Boolean:
                    {
                        await this.WriteBooleanAsync((bool)value);
                        break;
                    }
                case DataType.Byte:
                    {
                        await this.WriteByteAsync((sbyte)value);
                        break;
                    }
                case DataType.UnsignedByte:
                    {
                        await this.WriteUnsignedByteAsync((byte)value);
                        break;
                    }
                case DataType.Short:
                    {
                        await this.WriteShortAsync((short)value);
                        break;
                    }
                case DataType.UnsignedShort:
                    {
                        await this.WriteUnsignedShortAsync((ushort)value);
                        break;
                    }
                case DataType.Int:
                    {
                        await this.WriteIntAsync((int)value);
                        break;
                    }
                case DataType.Long:
                    {
                        await this.WriteLongAsync((long)value);
                        break;
                    }
                case DataType.Float:
                    {
                        await this.WriteFloatAsync((float)value);
                        break;
                    }
                case DataType.Double:
                    {
                        await this.WriteDoubleAsync((double)value);
                        break;
                    }

                case DataType.String:
                    {
                        // TODO: add casing options on Field attribute and support custom naming enums.
                        var val = value.GetType().IsEnum ? value.ToString().ToCamelCase() : value.ToString();
                        await Program.PacketLogger.LogDebugAsync($"Writing string: {val}");
                        await this.WriteStringAsync(val, length);
                        break;
                    }
                case DataType.Chat:
                    {
                        await this.WriteChatAsync((ChatMessage)value);
                        break;
                    }
                case DataType.VarInt:
                    {
                        await this.WriteVarIntAsync((int)value);
                        break;
                    }
                case DataType.VarLong:
                    {
                        await this.WriteVarLongAsync((long)value);
                        break;
                    }
                case DataType.Position:
                    {
                        if (value is Position position)
                        {
                            if (attribute.Absolute)
                            {
                                await this.WriteDoubleAsync(position.X);
                                await this.WriteDoubleAsync(position.Y);
                                await this.WriteDoubleAsync(position.Z);
                                break;
                            }

                            await this.WritePositionAsync(position);
                        }
                        else if (value is Transform transform)
                        {
                            await this.WriteDoubleAsync(transform.X);
                            await this.WriteDoubleAsync(transform.Y);
                            await this.WriteDoubleAsync(transform.Z);
                            await this.WriteFloatAsync(transform.Yaw.Degrees);
                            await this.WriteFloatAsync(transform.Pitch.Degrees);
                        }
                        else if (value is SoundPosition soundPosition)
                        {
                            await this.WriteIntAsync(soundPosition.X);
                            await this.WriteIntAsync(soundPosition.Y);
                            await this.WriteIntAsync(soundPosition.Z);
                        }

                        break;
                    }
                case DataType.Velocity:
                    {
                        var velocity = (Velocity)value;

                        await this.WriteShortAsync(velocity.X);
                        await this.WriteShortAsync(velocity.Y);
                        await this.WriteShortAsync(velocity.Z);
                        break;
                    }
                case DataType.UUID:
                    {
                        await this.WriteUuidAsync((Guid)value);
                        break;
                    }
                case DataType.Array:
                    {
                        if (value is List<CommandNode> nodes)
                        {
                            //await Program.PacketLogger.LogDebugAsync($"Commands: {string.Join(", ", nodes.Select(x => x.Name))} ({nodes.Count})");

                            foreach (var node in nodes)
                            {
                                await node.CopyToAsync(this);
                            }
                        }
                        else if (value is List<PlayerInfoAction> actions)
                        {
                            await this.WriteVarIntAsync(actions.Count);

                            foreach (var action in actions)
                                await action.WriteAsync(this);
                        }
                        break;
                    }
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }


        public async Task WriteLongArrayAsync(long[] values)
        {
            foreach (var value in values)
                await this.WriteLongAsync(value);
        }

        public async Task WriteLongArrayAsync(ulong[] values)
        {
            foreach (var value in values)
                await this.WriteLongAsync((long)value);
        }

        public async Task WriteVarLongAsync(long value)
        {
#if PACKETLOG
            await Program.PacketLogger.LogDebugAsync($"Writing VarLong ({value})");
#endif

            var v = (ulong)value;

            do
            {
                var temp = (byte)(v & 127);

                v >>= 7;

                if (v != 0)
                    temp |= 128;


                await this.WriteUnsignedByteAsync(temp);
            } while (v != 0);
        }

        public async Task WritePositionAsync(Position value, bool pre14 = true)
        {
            var pos = (((long)value.X & 0x3FFFFFF) << 38) | (((long)value.Y & 0xFFF) << 26) | ((long)value.Z & 0x3FFFFFF);

            await WriteLongAsync(pos);
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
            double x = (int)(value >> 38),
                y = (int)(value >> 26) & 0xFFF,
                z = (int)(value << 38 >> 38);

            if (PacketHandler.Protocol == ProtocolVersion.v1_14)
            {
                x = (int)(value >> 38);
                y = (int)value & 0xFFF;
                z = (int)(value << 26 >> 38);
            }

            if (x >= Math.Pow(2, 25)) { x -= Math.Pow(2, 26); }
            if (y >= Math.Pow(2, 11)) { y -= Math.Pow(2, 12); }
            if (z >= Math.Pow(2, 25)) { z -= Math.Pow(2, 26); }

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