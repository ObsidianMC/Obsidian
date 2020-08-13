using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using Obsidian.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Obsidian.Chat;
using Obsidian.Util.DataTypes;

namespace Obsidian.Serializer
{
    public static class PacketSerializer
    {
        private const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        public static async Task SerializeAsync(Packet packet, MinecraftStream stream)
        {
            if (packet == null)
                throw new ArgumentNullException(nameof(packet));

            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            await stream.Lock.WaitAsync();

            var valueDict = (await packet.GetAllObjectsAsync()).OrderBy(x => x.Key.Order);

            await using var dataStream = new MinecraftStream(true);

            foreach (var (key, value) in valueDict)
            {
                await Program.PacketLogger.LogDebugAsync($"Writing value @ {dataStream.Position}: {value} ({value.GetType()})");

                var dataType = key.Type;

                if (dataType == DataType.Auto)
                    dataType = value.GetType().ToDataType();

                await WriteToStream(dataStream, dataType, value);
            }

            var packetLength = packet.id.GetVarIntLength() + (int)dataStream.Length;

            await stream.WriteVarIntAsync(packetLength);
            await stream.WriteVarIntAsync(packet.id);

            dataStream.Position = 0;
            await dataStream.DumpAsync();

            dataStream.Position = 0;
            await dataStream.CopyToAsync(stream);

            stream.Lock.Release();
        }

        private static async Task WriteToStream(MinecraftStream stream, DataType type, object value, int length = 32767)
        {
            switch (type)
            {
                case DataType.Auto:
                {
                    await stream.WriteAutoAsync(value);
                    break;
                }
                case DataType.Boolean:
                {
                    await stream.WriteBooleanAsync((bool)value);
                    break;
                }
                case DataType.Byte:
                {
                    await stream.WriteByteAsync((sbyte)value);
                    break;
                }
                case DataType.UnsignedByte:
                {
                    await stream.WriteUnsignedByteAsync((byte)value);
                    break;
                }
                case DataType.Short:
                {
                    await stream.WriteShortAsync((short)value);
                    break;
                }
                case DataType.UnsignedShort:
                {
                    await stream.WriteUnsignedShortAsync((ushort)value);
                    break;
                }
                case DataType.Int:
                {
                    await stream.WriteIntAsync((int)value);
                    break;
                }
                case DataType.Long:
                {
                    await stream.WriteLongAsync((long)value);
                    break;
                }
                case DataType.Float:
                {
                    await stream.WriteFloatAsync((float)value);
                    break;
                }
                case DataType.Double:
                {
                    await stream.WriteDoubleAsync((double)value);
                    break;
                }
                
                case DataType.String:
                {
                    // TODO: add casing options on Field attribute and support custom naming enums.
                    await stream.WriteStringAsync(type.ToString().ToCamelCase(), length);
                    break;
                }
                case DataType.Chat:
                {
                    await stream.WriteChatAsync((ChatMessage)value);
                    break;
                }
                case DataType.VarInt:
                {
                    await stream.WriteVarIntAsync((int)value);
                    break;
                }
                case DataType.VarLong:
                {
                    await stream.WriteVarLongAsync((long)value);
                    break;
                }
                case DataType.Position:
                {
                    var pos = (Position) value;
                    
                    // if (absolute)
                    // {
                    //     await stream.WriteDoubleAsync(pos.X);
                    //     await stream.WriteDoubleAsync(pos.Y);
                    //     await stream.WriteDoubleAsync(pos.Z);
                    //     break;
                    // }

                    await stream.WritePositionAsync(pos);
                    break;
                }
                case DataType.UUID:
                {
                    await stream.WriteUuidAsync((Guid)value);
                    break;
                }
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        private static DataType ToDataType(this Type type)
        {
            if (type == typeof(ChatMessage))
                return DataType.Chat;
            
            if (type == typeof(Position))
                return DataType.Position;
            
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Int32:
                case TypeCode.UInt32:
                    throw new ArgumentException("A data type must be specified for integer values.", nameof(type));
                
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    throw new ArgumentException("A data type must be specified for integer values.",  nameof(type));
                
                case TypeCode.Boolean:
                    return DataType.Boolean;

                case TypeCode.SByte:
                    return DataType.Byte;

                case TypeCode.Byte:
                    return DataType.UnsignedByte;

                case TypeCode.Int16:
                    return DataType.Short;

                case TypeCode.UInt16:
                    return DataType.UnsignedShort;

                case TypeCode.Single:
                    return DataType.Float;

                case TypeCode.Double:
                    return DataType.Double;

                case TypeCode.String:
                    return DataType.String;

                case TypeCode.DateTime:
                case TypeCode.Empty:
                case TypeCode.Object:
                case TypeCode.DBNull:
                case TypeCode.Decimal:
                case TypeCode.Char:
                    throw new ArgumentOutOfRangeException(nameof(type));
                
                default:
                    return DataType.Auto;
            }
        }

        public static async Task<T> DeserializeAsync<T>(byte[] data) where T : Packet
        {
            await using var stream = new MinecraftStream(data);
            var packet = (T)Activator.CreateInstance(typeof(T));
            if (packet == null)
                throw new NullReferenceException(nameof(packet));

            var valueDict = (await packet.GetAllMemberNamesAsync()).OrderBy(x => x.Key.Order);
            var members = packet.GetType().GetMembers(flags);

            foreach (var (key, value) in valueDict)
            {
                foreach (var member in members)
                {
                    if (member.Name != value)
                        continue;

                    if (member is FieldInfo field)
                    {
                        var val = await stream.ReadAutoAsync(field.FieldType, key.Absolute, key.CountLength);

                        await Program.PacketLogger.LogDebugAsync($"Setting val {val}");

                        field.SetValue(packet, val);
                    }
                    else if (member is PropertyInfo property)
                    {
                        var val = await stream.ReadAutoAsync(property.PropertyType, key.Absolute, key.CountLength);

                        await Program.PacketLogger.LogDebugAsync($"Setting val {val}");

                        property.SetValue(packet, val);
                    }
                }
            }

            return packet;
        }

        private static async Task<Dictionary<FieldAttribute, string>> GetAllMemberNamesAsync(this Packet packet)
        {
            var members = packet.GetType().GetMembers(flags);
            var valueDict = new Dictionary<FieldAttribute, string>();

            foreach (var member in members)
            {
                var att = (FieldAttribute)Attribute.GetCustomAttribute(member, typeof(FieldAttribute));
                if (att == null)
                    continue;

                await Program.PacketLogger.LogDebugAsync($"Adding Member {member.Name}");
                valueDict.Add(att, member.Name);
            }

            return valueDict;
        }

        private static async Task<Dictionary<FieldAttribute, object>> GetAllObjectsAsync(this Packet packet)
        {
            var members = packet.GetType().GetMembers(flags);
            var valueDict = new Dictionary<FieldAttribute, object>();

            foreach (var member in members)
            {
                var att = (FieldAttribute)Attribute.GetCustomAttribute(member, typeof(FieldAttribute));
                if (att == null)
                    continue;

                if (member is FieldInfo field)
                {
                    var val = field.GetValue(packet);
                    await Program.PacketLogger.LogDebugAsync($"Adding val {val.GetType()}");
                    valueDict.Add(att, val);
                }
                else if (member is PropertyInfo property)
                {
                    var val = property.GetValue(packet);
                    await Program.PacketLogger.LogDebugAsync($"Adding val {(val.GetType().IsEnum ? val.GetType().BaseType : val.GetType())}: ({val})");
                    valueDict.Add(att, val);
                }
            }

            return valueDict;
        }
    }
}