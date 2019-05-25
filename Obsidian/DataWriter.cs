using Obsidian.Entities;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

//https://wiki.vg/Protocol#Data_types
public static class DataWriter
{
    public static async Task WriteByteAsync(this Stream stream, sbyte value) => await stream.WriteUnsignedByteAsync((byte)value);

    public static async Task WriteUnsignedByteAsync(this Stream stream, byte value) => await stream.WriteAsync(new[] { value });

    public static async Task WriteBooleanAsync(this Stream stream, bool value) => await stream.WriteByteAsync((sbyte)(value ? 0x01 : 0x00));

    public static async Task WriteUnsignedShortAsync(this Stream stream, ushort value)
    {
        var write = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(write);
        }
        await stream.WriteAsync(write);
    }

    public static async Task WriteShortAsync(this Stream stream, short value)
    {
        var write = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(write);
        }
        await stream.WriteAsync(write);
    }

    public static async Task WriteIntAsync(this Stream stream, int value)
    {
        var write = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(write);
        }
        await stream.WriteAsync(write);
    }

    public static async Task WriteLongAsync(this Stream stream, long value)
    {
        var write = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(write);
        }
        await stream.WriteAsync(write);
    }

    public static async Task WriteFloatAsync(this Stream stream, float value)
    {
        var write = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(write);
        }
        await stream.WriteAsync(write);
    }

    public static async Task WriteDoubleAsync(this Stream stream, double value)
    {
        var write = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(write);
        }
        await stream.WriteAsync(write);
    }

    public static async Task WriteStringAsync(this Stream stream, string value, int maxLength = 0)
    {
        if (maxLength > 0 && value.Length > maxLength)
        {
            throw new ArgumentException($"string ({value.Length}) exceeded maximum length ({maxLength})", nameof(value));
        }
        var bytes = Encoding.UTF8.GetBytes(value);
        await stream.WriteVarIntAsync(bytes.Length);
        await stream.WriteAsync(bytes);
    }

    public static async Task WriteChatAsync(this Stream stream, ChatMessage value) => await stream.WriteStringAsync(value.ToString(), 32767);

    public static async Task WriteIdentifierAsync(this Stream stream, string value) => await stream.WriteStringAsync(value, 32767);

    public static async Task WriteVarIntAsync(this Stream stream, int value)
    {
        while ((value & 128) != 0)
        {
            await stream.WriteUnsignedByteAsync((byte)(value & 127 | 128));
            value = (int)((uint)value) >> 7;
        }
        await stream.WriteUnsignedByteAsync((byte)value);
    }

    public static int GetVarintLength(this int val)
    {
        int amount = 0;
        int value = val;
        do
        {
            var temp = (sbyte)(value & 0b01111111);
            // Note: >>> means that the sign bit is shifted with the rest of the number rather than being left alone
            value >>= 7;
            if (value != 0)
            {
                temp |= 127;
            }
            amount++;
        } while (value != 0);
        return amount;
    }

    public static async Task WriteVarLongAsync(this Stream stream, long value)
    {
        do
        {
            var temp = (sbyte)(value & 0b01111111);
            // Note: >>> means that the sign bit is shifted with the rest of the number rather than being left alone
            value >>= 7;
            if (value != 0)
            {
                temp |= 127;
            }
            await stream.WriteByteAsync(temp);
        } while (value != 0);
    }

    public static async Task WritePositionAsync(this Stream stream, Position value) => await stream.WriteLongAsync((((value.X & 0x3FFFFFF) << 38) | ((value.Y & 0xFFF) << 26) | (value.Z & 0x3FFFFFF)));
}