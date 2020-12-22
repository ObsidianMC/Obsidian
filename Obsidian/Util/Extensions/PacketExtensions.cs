using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace Obsidian.Util.Extensions
{
    public static class PacketExtensions
    {
        public const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        internal static async Task WriteAsync(this IPacket packet, MinecraftStream stream)
        {
            await stream.Lock.WaitAsync();

            await using var dataStream = new MinecraftStream();
            await packet.WriteAsync(dataStream);

            var packetLength = packet.Id.GetVarIntLength() + (int)dataStream.Length;

            await stream.WriteVarIntAsync(packetLength);
            await stream.WriteVarIntAsync(packet.Id);

            dataStream.Position = 0;
            await dataStream.CopyToAsync(stream);

            stream.Lock.Release();
        }

        internal static Dictionary<FieldAttribute, string> GetAllMemberNames(this IPacket packet)
        {
            var members = packet.GetType().GetMembers(Flags);
            var valueDict = new Dictionary<FieldAttribute, string>();

            foreach (var member in members)
            {
                var att = (FieldAttribute)Attribute.GetCustomAttribute(member, typeof(FieldAttribute));
                if (att == null)
                    continue;

                //Globals.PacketLogger.LogDebug($"Adding Member {member.Name}");
                valueDict.Add(att, member.Name);
            }

            return valueDict;
        }

        internal static Dictionary<FieldAttribute, object> GetAllObjects(this IPacket packet)
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
                    //Globals.PacketLogger.LogDebug($"Adding val {(val.GetType().IsEnum ? val.GetType().BaseType : val.GetType())}: ({val})");
                    valueDict.Add(att, val);
                }
                else if (member is PropertyInfo property)
                {
                    var val = property.GetValue(packet);
                    //Globals.PacketLogger.LogDebug($"Adding val {(val.GetType().IsEnum ? val.GetType().BaseType : val.GetType())}: ({val})");
                    valueDict.Add(att, val);
                }
            }

            return valueDict;
        }

        internal static Dictionary<FieldAttribute, (string name, object value)> GetAllObjectsAndNames(this IPacket packet)
        {
            var members = packet.GetType().GetMembers(Flags);
            var valueDict = new Dictionary<FieldAttribute, (string, object)>();

            foreach (var member in members)
            {
                var att = (FieldAttribute)Attribute.GetCustomAttribute(member, typeof(FieldAttribute));
                if (att == null)
                    continue;

                if (member is FieldInfo field)
                {
                    var val = field.GetValue(packet);
                    //Globals.PacketLogger.LogDebug($"Adding val {(val.GetType().IsEnum ? val.GetType().BaseType : val.GetType())}: ({val})");
                    valueDict.Add(att, (field.Name, val));
                }
                else if (member is PropertyInfo property)
                {
                    var val = property.GetValue(packet);
                    //Globals.PacketLogger.LogDebug($"Adding val {(val.GetType().IsEnum ? val.GetType().BaseType : val.GetType())}: ({val})");
                    valueDict.Add(att, (property.Name, val));
                }
            }

            return valueDict;
        }
    }
}
