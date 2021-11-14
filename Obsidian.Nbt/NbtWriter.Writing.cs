using System;
using System.Linq;
using System.Text;

namespace Obsidian.Nbt;

public partial class NbtWriter
{
    internal void Write(NbtTagType tagType) => this.WriteByteInternal((byte)tagType);

    internal void WriteByteInternal(byte value) => this.BaseStream.WriteByte(value);

    internal void WriteStringInternal(string value)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));

        if (value.Length > short.MaxValue)
            throw new InvalidOperationException($"value length must be less than {short.MaxValue}");

        var buffer = Encoding.UTF8.GetBytes(value);

        this.WriteShortInternal((short)buffer.Length);
        this.BaseStream.Write(buffer);
    }

    internal void WriteShortInternal(short value)
    {
        Span<byte> buffer = stackalloc byte[2];

        BitConverter.TryWriteBytes(buffer, value);

        if (BitConverter.IsLittleEndian)
            buffer.Reverse();

        this.BaseStream.Write(buffer);
    }

    internal void WriteIntInternal(int value)
    {
        Span<byte> buffer = stackalloc byte[4];

        BitConverter.TryWriteBytes(buffer, value);

        if (BitConverter.IsLittleEndian)
            buffer.Reverse();

        this.BaseStream.Write(buffer);
    }

    internal void WriteFloatInternal(float value)
    {
        Span<byte> buffer = stackalloc byte[4];

        BitConverter.TryWriteBytes(buffer, value);

        if (BitConverter.IsLittleEndian)
            buffer.Reverse();

        this.BaseStream.Write(buffer);
    }

    internal void WriteLongInternal(long value)
    {
        Span<byte> buffer = stackalloc byte[8];

        BitConverter.TryWriteBytes(buffer, value);

        if (BitConverter.IsLittleEndian)
            buffer.Reverse();

        this.BaseStream.Write(buffer);
    }

    internal void WriteDoubleInternal(double value)
    {
        Span<byte> buffer = stackalloc byte[8];

        BitConverter.TryWriteBytes(buffer, value);

        if (BitConverter.IsLittleEndian)
            buffer.Reverse();

        this.BaseStream.Write(buffer);
    }
}
