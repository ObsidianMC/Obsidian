using Obsidian.API;
using Obsidian.Nbt;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// The packets and vanilla types <c>PacketSerializationGenerator</c> generates from how vanilla writes them.
/// </summary>
public class GeneratedPackets
{
    [Fact(DisplayName = "A generated clientbound packet writes its fields in vanilla's order and encodings")]
    public void SerializesInVanillaOrder()
    {
        using var buffer = new NetworkBuffer();
        new CommandSuggestionsPacket
        {
            IdValue = 5,
            Start = 1,
            Length = 2,
            Suggestions = [new CommandSuggestionsEntry { Text = "ab", Tooltip = null }]
        }.Serialize(buffer);

        // VarInts 5, 1, 2; a list of one entry: the string "ab", then no tooltip.
        Assert.Equal([0x05, 0x01, 0x02, 0x01, 0x02, (byte)'a', (byte)'b', 0x00], Written(buffer));
    }

    [Fact(DisplayName = "A generated serverbound packet reads vanilla's encoding")]
    public void PopulatesFromVanillaBytes()
    {
        using var buffer = new NetworkBuffer([0x01]);
        var packet = new ChangeGameModePacket();

        packet.Populate(buffer);

        Assert.Equal(GameMode.Creative, packet.Mode);
    }

    [Theory(DisplayName = "Generated vanilla types read back what they write")]
    [MemberData(nameof(GeneratedRecords))]
    public void RecordsRoundTrip(Type type)
    {
        var write = type.GetMethod("Write", BindingFlags.Public | BindingFlags.Static)!;
        var read = type.GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!;

        var written = Write(write, Sample(type, depth: 0));
        using var input = new NetworkBuffer(written);
        var value = read.Invoke(null, [input]);

        Assert.Equal(written.Length, input.Offset);
        Assert.Equal(written, Write(write, value));
    }

    public static IEnumerable<object[]> GeneratedRecords() =>
        typeof(GameMode).Assembly.GetTypes()
            .Where(type => type.IsClass && type.GetCustomAttribute<GeneratedCodeAttribute>()?.Tool == "Obsidian.SourceGenerators"
                && type.GetInterfaces().Any(implemented => implemented.IsGenericType
                    && implemented.GetGenericTypeDefinition() == typeof(INetworkSerializable<>)))
            .OrderBy(type => type.Name)
            .Select(type => new object[] { type });

    private static byte[] Write(MethodInfo write, object? value)
    {
        using var buffer = new NetworkBuffer();
        write.Invoke(null, [value, buffer]);
        return Written(buffer);
    }

    // ToArray is the whole backing array; the written bytes are its first Size.
    private static byte[] Written(NetworkBuffer buffer) => buffer.AsSpan(0, buffer.Size).ToArray();

    /// <summary>A value of every type generated code reads and writes, with records filled in recursively.</summary>
    private static object? Sample(Type type, int depth)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsEnum)
            return Enum.GetValues(type).Cast<object>().Last();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var list = (IList)Activator.CreateInstance(type)!;
            if (depth < 3)
                list.Add(Sample(type.GetGenericArguments()[0], depth + 1));
            return list;
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            var map = (IDictionary)Activator.CreateInstance(type)!;
            var arguments = type.GetGenericArguments();
            map.Add(Sample(arguments[0], depth + 1)!, Sample(arguments[1], depth + 1));
            return map;
        }

        return type switch
        {
            _ when type == typeof(bool) => true,
            _ when type == typeof(sbyte) => (sbyte)-6,
            _ when type == typeof(short) => (short)300,
            _ when type == typeof(int) => 3,
            _ when type == typeof(long) => 4L,
            _ when type == typeof(float) => 1.5f,
            _ when type == typeof(double) => 2.5,
            _ when type == typeof(string) => "obsidian",
            _ when type == typeof(Guid) => new Guid("0b5d1c3e-8f2a-4e6b-9c7d-1a2b3c4d5e6f"),
            _ when type == typeof(DateTimeOffset) => DateTimeOffset.FromUnixTimeMilliseconds(1_000),
            _ when type == typeof(Vector) => new Vector(1, 2, 3),
            _ when type == typeof(VectorF) => new VectorF(1.5f, 2, 3),
            _ when type == typeof(Velocity) => new Velocity(0.25, 0, -0.5),
            _ when type == typeof(Angle) => new Angle(64),
            _ when type == typeof(ChatMessage) => ChatMessage.Simple("hi"),
            _ when type == typeof(NbtCompound) => new NbtCompound(),
            _ when type == typeof(byte[]) => new byte[] { 1, 2, 3 },
            _ when type == typeof(long[]) => new long[] { 1, 2 },
            _ when type == typeof(int[]) => new[] { 1, 2 },
            _ when type == typeof(BitSet) => new BitSet([5L]),
            _ when type.GetCustomAttribute<GeneratedCodeAttribute>() is not null => Record(type, depth),
            _ => null,
        };
    }

    private static object Record(Type type, int depth)
    {
        var record = Activator.CreateInstance(type)!;
        foreach (var property in type.GetProperties().Where(property => property.CanWrite))
            property.SetValue(record, Sample(property.PropertyType, depth + 1));
        return record;
    }
}
