using Obsidian.Chat;
using Obsidian.GameState;
using Obsidian.Net.Packets;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using Obsidian.Util.DataTypes;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace Obsidian.Util.Extensions
{
    public static class PacketExtensions
    {
        public const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        internal static DataType ToDataType(this Type type)
        {
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Int32:
                    return DataType.Int;

                case TypeCode.Int64:
                    return DataType.Long;

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

                case TypeCode.Empty:
                    throw new NullReferenceException(nameof(type));

                case TypeCode.UInt32:
                case TypeCode.UInt64:
                    throw new ArgumentException("Invalid data type. Please use int or long", nameof(type));

                case TypeCode.DateTime:
                case TypeCode.Object:
                    {
                        if (type == typeof(ChatMessage))
                            return DataType.Chat;
                        else if (type == typeof(Position))
                            return DataType.Position;
                        else if (type == typeof(SoundPosition))
                            return DataType.SoundPosition;
                        else if (type == typeof(Velocity))
                            return DataType.Velocity;
                        else if (type == typeof(Transform))
                            return DataType.Transform;
                        else if (type == typeof(ChangeGameStateReason))
                            return DataType.ChangeGameStateReason;
                        else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IList))
                            return DataType.Array;

                        return DataType.Auto;
                    }
                case TypeCode.DBNull:
                case TypeCode.Decimal:
                case TypeCode.Char:
                    throw new NotSupportedException(nameof(type));

                default:
                    return DataType.Auto;
            }
        }

        internal static async Task<Dictionary<FieldAttribute, string>> GetAllMemberNamesAsync(this Packet packet)
        {
            var members = packet.GetType().GetMembers(Flags);
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

        internal static async Task<Dictionary<FieldAttribute, object>> GetAllObjectsAsync(this Packet packet)
        {
            var members = packet.GetType().GetMembers(Flags);
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
