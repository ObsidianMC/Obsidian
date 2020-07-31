using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Serializer.Attributes;
using Obsidian.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian.Serializer
{
    public static class PacketSerializer
    {
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

            var fields = packet.GetType().GetFields();
            var properties = packet.GetType().GetProperties();
            foreach (var (key, value) in valueDict)
            {
                foreach (var field in fields)
                {
                    if (field.Name == value)
                    {
                        var val = await stream.ReadAutoAsync(field.FieldType, key.Absolute, key.CountLength);

                        Console.WriteLine($"Setting val {val}");

                        field.SetValue(packet, val);
                    }
                }

                foreach (var property in properties)
                {
                    if (property.Name == value)
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
            var fields = packet.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var properties = packet.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var valueDict = new Dictionary<PacketOrderAttribute, string>();

            foreach (var field in fields)
            {
                var att = (PacketOrderAttribute)Attribute.GetCustomAttribute(field, typeof(PacketOrderAttribute));

                if (att != null)
                    valueDict.Add(att, field.Name);
            }

            foreach (var property in properties)
            {
                var att = (PacketOrderAttribute)Attribute.GetCustomAttribute(property, typeof(PacketOrderAttribute));

                if (att != null)
                    valueDict.Add(att, property.Name);
            }

            return valueDict;
        }

        public static Dictionary<PacketOrderAttribute, object> GetAllObjects(this Packet packet)
        {
            var fields = packet.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var properties = packet.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var valueDict = new Dictionary<PacketOrderAttribute, object>();

            foreach (var field in fields)
            {
                var att = (PacketOrderAttribute)Attribute.GetCustomAttribute(field, typeof(PacketOrderAttribute));

                if (att != null)
                {
                    var val = field.GetValue(packet);
                    var e = false;
                    if (val is Enum)
                    {
                        e = true;
                    }
                    Console.WriteLine($"Adding val {val.GetType()}:{e}");
                    valueDict.Add(att, val);
                }
            }

            foreach (var property in properties)
            {
                var att = (PacketOrderAttribute)Attribute.GetCustomAttribute(property, typeof(PacketOrderAttribute));

                if (att != null)
                {
                    var val = property.GetValue(packet);
                    var e = false;
                    if (val is Enum)
                    {
                        e = true;
                    }
                    Console.WriteLine($"Adding val {val.GetType()}:{e}");
                    valueDict.Add(att, val);
                }
            }

            return valueDict;
        }
    }
}