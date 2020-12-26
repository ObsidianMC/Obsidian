using Obsidian.API;
using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serializer.Dynamic;
using Obsidian.Serializer.Enums;
using Obsidian.Util.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Obsidian.Serializer
{
    public static class PacketSerializer
    {
        internal delegate IPacket DeserializeDelegate(MinecraftStream minecraftStream);

        private static Dictionary<Type, DeserializeDelegate> deserializationMethodsCache = new Dictionary<Type, DeserializeDelegate>();

        public static async Task SerializeAsync(IPacket packet, MinecraftStream stream)
        {
            try
            {
                if (packet == null)
                    throw new ArgumentNullException(nameof(packet));

                if (stream == null)
                    throw new ArgumentNullException(nameof(stream));

                await stream.Lock.WaitAsync();

                await using var dataStream = new MinecraftStream();

                if (packet is ChunkDataPacket chunkData)
                {
                    await chunkData.WriteAsync(dataStream);
                }
                else if (packet is DeclareRecipes recipes)
                {
                    await recipes.WriteAsync(dataStream);
                }
                else
                {
                    var valueDict = packet.GetAllObjects().OrderBy(x => x.Key.Order);

                    foreach (var (key, value) in valueDict)
                    {
                        //await Globals.PacketLogger.LogDebugAsync($"Writing value @ {dataStream.Position}: {value} ({value.GetType()})");

                        var dataType = key.Type;

                        if (dataType == DataType.Auto)
                            dataType = value.GetType().ToDataType();

                        await dataStream.WriteAsync(dataType, key, value);
                    }
                }

                var packetLength = packet.Id.GetVarIntLength() + (int)dataStream.Length;

                await stream.WriteVarIntAsync(packetLength);
                await stream.WriteVarIntAsync(packet.Id);

                dataStream.Position = 0;
                // await dataStream.DumpAsync(packet: packet);

                await dataStream.CopyToAsync(stream);
            }
            catch
            {
                throw;
            }
            finally
            {
                stream.Lock.Release();
            }
        }

        public static async Task<T> DeserializeAsync<T>(byte[] data) where T : IPacket
        {
            await using var stream = new MinecraftStream(data);
            var packet = (T)Activator.CreateInstance(typeof(T));//TODO make sure all packets have default constructors

            if (packet == null)
                throw new NullReferenceException(nameof(packet));

            //Globals.PacketLogger.LogDebug($"Deserializing {packet}");

            var valueDict = packet.GetAllMemberNames().OrderBy(x => x.Key.Order);
            var members = packet.GetType().GetMembers(PacketExtensions.Flags);

            int readableBytes = 0;
            foreach (var (key, value) in valueDict)
            {
                var member = members.FirstOrDefault(x => x.Name.EqualsIgnoreCase(value));

                if (member is FieldInfo field)
                {
                    var dataType = key.Type;

                    if (dataType == DataType.Auto)
                        dataType = field.FieldType.ToDataType();

                    var val = await stream.ReadAsync(field.FieldType, dataType, key);

                    //Globals.PacketLogger.LogDebug($"Setting val {val}");

                    field.SetValue(packet, val);
                }
                else if (member is PropertyInfo property)
                {
                    var dataType = key.Type;

                    if (dataType == DataType.Auto)
                        dataType = property.PropertyType.ToDataType();

                    var val = await stream.ReadAsync(property.PropertyType, dataType, key, readableBytes);

                    //Globals.PacketLogger.LogDebug($"Setting val {val}");

                    if (property.PropertyType.IsEnum && property.PropertyType == typeof(BlockFace))
                        val = (BlockFace)val;

                    property.SetValue(packet, val);
                }

                readableBytes = data.Length - (int)stream.Position;
            }

            return packet;
        }

        public static async Task<T> FastDeserializeAsync<T>(byte[] data) where T : IPacket
        {
            await using var stream = new MinecraftStream(data);

            if (!deserializationMethodsCache.TryGetValue(typeof(T), out var deserializeMethod))
                deserializationMethodsCache.Add(typeof(T), deserializeMethod = SerializationMethodBuilder.BuildDeserializationMethod<T>());

            return (T)deserializeMethod(stream);
        }

        public static T FastDeserialize<T>(byte[] data) where T : IPacket
        {
            using var stream = new MinecraftStream(data);

            if (!deserializationMethodsCache.TryGetValue(typeof(T), out var deserializeMethod))
                deserializationMethodsCache.Add(typeof(T), deserializeMethod = SerializationMethodBuilder.BuildDeserializationMethod<T>());

            return (T)deserializeMethod(stream);
        }

        public static T FastDeserialize<T>(MinecraftStream minecraftStream) where T : IPacket
        {
            if (!deserializationMethodsCache.TryGetValue(typeof(T), out var deserializeMethod))
                deserializationMethodsCache.Add(typeof(T), deserializeMethod = SerializationMethodBuilder.BuildDeserializationMethod<T>());

            return (T)deserializeMethod(minecraftStream);
        }
    }
}