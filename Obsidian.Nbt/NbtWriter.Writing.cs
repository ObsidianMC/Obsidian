using System.Buffers.Binary;
using System.Text;

namespace Obsidian.Nbt;

public partial class NbtWriter
{
    internal void Write(NbtTagType tagType) => WriteByteInternal((byte)tagType);

    internal void WriteByteInternal(byte value) => BaseStream.WriteByte(value);

    internal void WriteStringInternal(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length > short.MaxValue)
            throw new InvalidOperationException($"value length must be less than {short.MaxValue}");

        var buffer = Encoding.UTF8.GetBytes(value);

        WriteShortInternal((short)buffer.Length);
        BaseStream.Write(buffer);
    }
    internal void WriteShortInternal(short value)
    {
        Span<byte> buffer = stackalloc byte[2];

        BinaryPrimitives.WriteInt16BigEndian(buffer, value);

        BaseStream.Write(buffer);
    }

    internal void WriteIntInternal(int value)
    {
        Span<byte> buffer = stackalloc byte[4];

        BinaryPrimitives.WriteInt32BigEndian(buffer, value);

        BaseStream.Write(buffer);
    }

    internal void WriteFloatInternal(float value)
    {
        Span<byte> buffer = stackalloc byte[4];

        BinaryPrimitives.WriteSingleBigEndian(buffer, value);

        BaseStream.Write(buffer);
    }

    internal void WriteLongInternal(long value)
    {
        Span<byte> buffer = stackalloc byte[8];

        BinaryPrimitives.WriteInt64BigEndian(buffer, value);

        BaseStream.Write(buffer);
    }

    internal void WriteDoubleInternal(double value)
    {
        Span<byte> buffer = stackalloc byte[8];

        BinaryPrimitives.WriteDoubleBigEndian(buffer, value);

        BaseStream.Write(buffer);
    }
}
