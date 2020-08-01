using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Serializer.Attributes;
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
        private static BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        public static async Task SerializeAsync(Packet packet, MinecraftStream stream)
        {
            if (packet == null)
                throw new ArgumentNullException(nameof(packet));
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            var valueDict = packet.GetAllObjects().OrderBy(x => x.Key.Order);

            await stream.Lock.WaitAsync();

            await using var dataStream = new MinecraftStream();

            foreach (var (key, value) in valueDict)
            {
                await dataStream.WriteAutoAsync(value, key.Absolute, key.CountLength);
            }

            var packetLength = packet.id.GetVarIntLength() + (int)dataStream.Length;

            await stream.WriteVarIntAsync(packetLength);
            await stream.WriteVarIntAsync(packet.id);

            dataStream.Position = 0;
            await dataStream.CopyToAsync(stream);

            stream.Lock.Release();
        }

        public static async Task<T> DeserializeAsync<T>(byte[] data) where T : Packet
        {
            await using var stream = new MinecraftStream(data);
            var packet = (T)Activator.CreateInstance(typeof(T));
            if (packet == null)
                throw new NullReferenceException(nameof(packet));

            var valueDict = packet.GetAllMemberNames().OrderBy(x => x.Key.Order);

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

                        Console.WriteLine($"Setting val {val}");

                        field.SetValue(packet, val);
                    }
                    else if (member is PropertyInfo property)
                    {
                        var val = await stream.ReadAutoAsync(property.PropertyType, key.Absolute, key.CountLength);

                        Console.WriteLine($"Setting val {val}");

                        property.SetValue(packet, val);
                    }
                }
            }

            return packet;
        }

        public static Dictionary<PacketOrderAttribute, string> GetAllMemberNames(this Packet packet)
        {
            MemberInfo[] members = packet.GetType().GetMembers(flags);

            var valueDict = new Dictionary<PacketOrderAttribute, string>();

            foreach (var member in members)
            {
                var att = (PacketOrderAttribute)Attribute.GetCustomAttribute(member, typeof(PacketOrderAttribute));
                if (att == null)
                    continue;

                Console.WriteLine($"Adding Member {member.Name}");
                valueDict.Add(att, member.Name);
            }

            return valueDict;
        }

        public static Dictionary<PacketOrderAttribute, object> GetAllObjects(this Packet packet)
        {
            MemberInfo[] members = packet.GetType().GetMembers(flags);

            var valueDict = new Dictionary<PacketOrderAttribute, object>();

            foreach (var member in members)
            {
                var att = (PacketOrderAttribute)Attribute.GetCustomAttribute(member, typeof(PacketOrderAttribute));
                if (att == null)
                    continue;

                if (member is FieldInfo field)
                {
                    var val = field.GetValue(packet);
                    Console.WriteLine($"Adding val {val.GetType()}");
                    valueDict.Add(att, val);
                }
                else if (member is PropertyInfo property)
                {
                    var val = property.GetValue(packet);
                    Console.WriteLine($"Adding val {val.GetType()}");
                    valueDict.Add(att, val);
                }
            }

            return valueDict;
        }
    }
}