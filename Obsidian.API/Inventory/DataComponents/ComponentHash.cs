using System.Buffers.Binary;
using System.Text;

namespace Obsidian.API.Inventory.DataComponents;

/// <summary>Vanilla HashOps value hashing: CRC32C, little-endian scalar payloads and sorted map-entry hashes.</summary>
public static class ComponentHash
{
    public static int Bytes(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0x82f63b78u);
        }

        return unchecked((int)~crc);
    }

    public static int Byte(byte value) => Bytes([6, value]);

    public static int Boolean(bool value) => Bytes([13, value ? (byte)1 : (byte)0]);

    public static int Number(byte tag, long bits, int width)
    {
        Span<byte> bytes = stackalloc byte[9];
        bytes[0] = tag;
        BinaryPrimitives.WriteInt64LittleEndian(bytes[1..], bits);
        return Bytes(bytes[..(width + 1)]);
    }

    public static int Int(int value) => Number(8, value, 4);

    public static int Float(float value) => Number(10, BitConverter.SingleToInt32Bits(value), 4);

    public static int String(string value)
    {
        var bytes = new byte[5 + value.Length * 2];
        bytes[0] = 12;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(1), value.Length);
        // Guava hashes Java UTF-16 code units, including unpaired surrogates.
        for (var i = 0; i < value.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(5 + i * 2), value[i]);

        return Bytes(bytes);
    }

    public static int List(IEnumerable<int> values)
    {
        var hashes = values.ToArray();
        var bytes = new byte[hashes.Length * 4 + 2];
        bytes[0] = 4;
        bytes[^1] = 5;
        for (var i = 0; i < hashes.Length; i++)
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(1 + i * 4), hashes[i]);

        return Bytes(bytes);
    }

    public static int Map(IEnumerable<(int Key, int Value)> values)
    {
        var entries = values.OrderBy(pair => unchecked((uint)pair.Key)).ThenBy(pair => unchecked((uint)pair.Value)).ToArray();
        var bytes = new byte[entries.Length * 8 + 2];
        bytes[0] = 2;
        bytes[^1] = 3;
        for (var i = 0; i < entries.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(1 + i * 8), entries[i].Key);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(5 + i * 8), entries[i].Value);
        }

        return Bytes(bytes);
    }

    public static int Record(params (string Key, int Value)[] values) => Map(values.Select(pair => (String(pair.Key), pair.Value)));
}
