using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Serializer.Enums;
using Obsidian.Util.Extensions;
using System;
using System.Linq;
using System.Reflection;
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

            await stream.Lock.WaitAsync();

            var valueDict = (await packet.GetAllObjectsAsync()).OrderBy(x => x.Key.Order);

            await using var dataStream = new MinecraftStream(true);

            foreach (var (key, value) in valueDict)
            {
                //await Program.PacketLogger.LogDebugAsync($"Writing value @ {dataStream.Position}: {value} ({value.GetType()})");

                var dataType = key.Type;

                if (dataType == DataType.Auto)
                    dataType = value.GetType().ToDataType();

                await dataStream.WriteAsync(dataType, key, value);
            }

            var packetLength = packet.id.GetVarIntLength() + (int)dataStream.Length;

            await stream.WriteVarIntAsync(packetLength);
            await stream.WriteVarIntAsync(packet.id);

            dataStream.Position = 0;
            await dataStream.DumpAsync();

            await dataStream.CopyToAsync(stream);

            stream.Lock.Release();
        }

        public static async Task<T> DeserializeAsync<T>(byte[] data) where T : Packet
        {
            await using var stream = new MinecraftStream(data);
            var packet = (T)Activator.CreateInstance(typeof(T));
            if (packet == null)
                throw new NullReferenceException(nameof(packet));

            var valueDict = (await packet.GetAllMemberNamesAsync()).OrderBy(x => x.Key.Order);
            var members = packet.GetType().GetMembers(PacketExtensions.Flags);

            foreach (var (key, value) in valueDict)
            {
                foreach (var member in members)
                {
                    if (member.Name != value)
                        continue;

                    if (member is FieldInfo field)
                    {
                        var dataType = key.Type;

                        if (dataType == DataType.Auto)
                            dataType = field.FieldType.ToDataType();

                        var val = await stream.ReadAsync(field.FieldType, dataType, key);

                        await Program.PacketLogger.LogDebugAsync($"Setting val {val}");

                        field.SetValue(packet, val);
                    }
                    else if (member is PropertyInfo property)
                    {
                        var dataType = key.Type;

                        if (dataType == DataType.Auto)
                            dataType = property.PropertyType.ToDataType();

                        var val = await stream.ReadAsync(property.PropertyType, dataType, key);

                        await Program.PacketLogger.LogDebugAsync($"Setting val {val}");

                        property.SetValue(packet, val);
                    }
                }
            }

            return packet;
        }


    }
}