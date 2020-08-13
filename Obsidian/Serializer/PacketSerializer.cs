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

namespace Obsidian.Serializer
{
    public static class PacketSerializer
    {
        private static readonly BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

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

        public static async Task WriteToStream(MinecraftStream stream, DataType type, object value, int length = 32767)
        {
            switch (type)
            {
                //case DataType.Auto:
                //{
                //    await dataStream.WriteAutoAsync(value, key.Absolute, key.CountLength);
                //    break;
                //}
                case DataType.Bool:
                {
                    await stream.WriteBooleanAsync((bool)value);
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
                case DataType.Long:
                {
                    await stream.WriteLongAsync((long)value);
                    break;
                }
                case DataType.UnsignedByte:
                {
                    await stream.WriteUnsignedByteAsync((byte)value);
                    break;
                }
                case DataType.VarInt:
                {
                    await stream.WriteVarIntAsync((int)value);
                    break;
                }
                case DataType.String:
                {
                    // TODO: add casing options on Field attribute and support custom naming enums.
                    await stream.WriteStringAsync(type.ToString().ToCamelCase(), length);
                    break;
                }
                case DataType.Int:
                {
                    await stream.WriteIntAsync((int)value);
                    break;
                }
            }
        }

        public static DataType ToDataType(this Type type)
        {
            return Type.GetTypeCode(type) switch
            {
                TypeCode.Empty => throw new NotImplementedException(),
                TypeCode.Object => throw new NotImplementedException(),
                TypeCode.DBNull => throw new NotImplementedException(),
                TypeCode.Boolean => throw new NotImplementedException(),
                TypeCode.Char => throw new NotImplementedException(),
                TypeCode.SByte => throw new NotImplementedException(),
                TypeCode.Byte => throw new NotImplementedException(),
                TypeCode.Int16 => throw new NotImplementedException(),
                TypeCode.UInt16 => throw new NotImplementedException(),
                TypeCode.Int32 => throw new NotImplementedException(),
                TypeCode.UInt32 => throw new NotImplementedException(),
                TypeCode.Int64 => throw new NotImplementedException(),
                TypeCode.UInt64 => throw new NotImplementedException(),
                TypeCode.Single => throw new NotImplementedException(),
                TypeCode.Double => throw new NotImplementedException(),
                TypeCode.Decimal => throw new NotImplementedException(),
                TypeCode.DateTime => throw new NotImplementedException(),
                TypeCode.String => throw new NotImplementedException(),
                _ => throw new NotImplementedException(),
            };
        }

        public static async Task<T> DeserializeAsync<T>(byte[] data) where T : Packet
        {
            await using var stream = new MinecraftStream(data);
            var packet = (T)Activator.CreateInstance(typeof(T));
            if (packet == null)
                throw new NullReferenceException(nameof(packet));

            var valueDict = (await packet.GetAllMemberNamesAsync()).OrderBy(x => x.Key.Order);

            MemberInfo[] members = packet.GetType().GetMembers(flags);

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

        public static async Task<Dictionary<FieldAttribute, string>> GetAllMemberNamesAsync(this Packet packet)
        {
            MemberInfo[] members = packet.GetType().GetMembers(flags);

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

        public static async Task<Dictionary<FieldAttribute, object>> GetAllObjectsAsync(this Packet packet)
        {
            MemberInfo[] members = packet.GetType().GetMembers(flags);

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