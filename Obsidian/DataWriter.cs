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

    public static async Task WriteUnsignedShortAsync(this Stream stream, ushort value) => await stream.WriteAsync(BitConverter.GetBytes(value));

    public static async Task WriteShortAsync(this Stream stream, short value) => await stream.WriteAsync(BitConverter.GetBytes(value));

    public static async Task WriteIntAsync(this Stream stream, uint value) => await stream.WriteAsync(BitConverter.GetBytes(value));

    public static async Task WriteLongAsync(this Stream stream, ulong value) => await stream.WriteAsync(BitConverter.GetBytes(value));

    public static async Task WriteFloatAsync(this Stream stream, float value) => await stream.WriteAsync(BitConverter.GetBytes(value));

    public static async Task WriteDoubleAsync(this Stream stream, double value) => await stream.WriteAsync(BitConverter.GetBytes(value));

    public static async Task WriteStringAsync(this Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        await stream.WriteVarIntAsync(bytes.Length);
        await stream.WriteAsync(bytes);
    }

    public static async Task WriteChatAsync(this Stream stream, Chat value)
    {
        if (value.ToString().Length > 32767)
        {
            throw new ArgumentException("string exceeded maximum length", nameof(value));
        }
        await stream.WriteStringAsync(value.ToString());
    }

    public static async Task WriteIdentifierAsync(this Stream stream, string value)
    {
        if (value.Length > 32767)
        {
            throw new ArgumentException("string exceeded maximum length", nameof(value));
        }
        await stream.WriteStringAsync(value);
    }

    public static async Task WriteVarIntAsync(this Stream stream, int value)
    {
        while ((value & 128) != 0)
        {
            await stream.WriteUnsignedByteAsync((byte)(value & 127 | 128));
            value = (int) ((uint) value) >> 7;
        }
        await stream.WriteUnsignedByteAsync((byte)value);
    }

    public static int GetVarintLength(this int val)
    {
        int amount = 0;
        int value = val;
        do
        {
            sbyte temp = (sbyte)(value & 0b01111111);
            // Note: >>> means that the sign bit is shifted with the rest of the number rather than being left alone
            value = value >> 7;
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
        do {
            sbyte temp = (sbyte)(value & 0b01111111);
            // Note: >>> means that the sign bit is shifted with the rest of the number rather than being left alone
            value = value >> 7;
            if (value != 0) {
                temp |= 127;
            }
            await stream.WriteByteAsync(temp);
        } while (value != 0);
    }
}