using Obsidian.Nbt.Utilities;
using System.Buffers.Binary;
using System.Text;

namespace Obsidian.Nbt;

public partial struct NbtWriterStream
{
    public void WriteString(string value)
    {
        this.Validate(null, NbtTagType.String);
        this.WriteStringInternal(value);
    }

    public void WriteString(string name, string value)
    {
        this.Validate(name, NbtTagType.String);

        this.Write(NbtTagType.String);
        this.WriteStringInternal(name);
        this.WriteStringInternal(value);
    }

    public void WriteByte(byte value)
    {
        this.Validate(null, NbtTagType.Byte);
        this.WriteByteInternal(value);
    }

    public void WriteByte(string name, byte value)
    {
        this.Validate(name, NbtTagType.Byte);

        this.Write(NbtTagType.Byte);
        this.WriteStringInternal(name);
        this.WriteByteInternal(value);
    }

    public void WriteBool(bool value)
    {
        this.Validate(null, NbtTagType.Byte);
        this.WriteByteInternal((byte)(value ? 1 : 0));
    }

    public void WriteBool(string name, bool value)
    {
        this.Validate(name, NbtTagType.Byte);

        this.Write(NbtTagType.Byte);
        this.WriteStringInternal(name);
        this.WriteByteInternal((byte)(value ? 1 : 0));
    }

    public void WriteShort(short value)
    {
        this.Validate(null, NbtTagType.Short);
        this.WriteShortInternal(value);
    }

    public void WriteShort(string name, short value)
    {
        this.Validate(name, NbtTagType.Short);

        this.Write(NbtTagType.Short);
        this.WriteStringInternal(name);
        this.WriteShortInternal(value);
    }

    public void WriteInt(int value)
    {
        this.Validate(null, NbtTagType.Int);
        this.WriteIntInternal(value);
    }

    public void WriteInt(string name, int value)
    {
        this.Validate(name, NbtTagType.Int);

        this.Write(NbtTagType.Int);
        this.WriteStringInternal(name);
        this.WriteIntInternal(value);
    }

    public void WriteLong(long value)
    {
        this.Validate(null, NbtTagType.Long);
        this.WriteLongInternal(value);
    }

    public void WriteLong(string name, long value)
    {
        this.Validate(name, NbtTagType.Long);

        this.Write(NbtTagType.Long);
        this.WriteStringInternal(name);
        this.WriteLongInternal(value);
    }

    public void WriteFloat(float value)
    {
        this.Validate(null, NbtTagType.Float);
        this.WriteFloatInternal(value);
    }

    public void WriteFloat(string name, float value)
    {
        this.Validate(name, NbtTagType.Float);

        this.Write(NbtTagType.Float);
        this.WriteStringInternal(name);
        this.WriteFloatInternal(value);
    }

    public void WriteDouble(double value)
    {
        this.Validate(null, NbtTagType.Double);
        this.WriteDoubleInternal(value);
    }

    public void WriteDouble(string name, double value)
    {
        this.Validate(name, NbtTagType.Double);

        this.Write(NbtTagType.Double);
        this.WriteStringInternal(name);

        this.WriteDoubleInternal(value);
    }

    public void Write(NbtTagType tagType) => this.WriteByteInternal((byte)tagType);

    private void WriteByteInternal(byte value) => this.BaseStream.WriteByte(value);

    private void WriteStringInternal(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length > short.MaxValue)
            throw new InvalidOperationException($"value length must be less than {short.MaxValue}");

        if (!ModifiedUtf8.TryGetBytes(value, out var buffer))
            throw new InvalidOperationException("Failed to get bytes from string.");

        this.WriteShortInternal((short)buffer.Length);
        this.BaseStream.Write(buffer);
    }
    private void WriteShortInternal(short value)
    {
        Span<byte> buffer = stackalloc byte[2];

        BinaryPrimitives.WriteInt16BigEndian(buffer, value);

        this.BaseStream.Write(buffer);
    }

    private void WriteIntInternal(int value)
    {
        Span<byte> buffer = stackalloc byte[4];

        BinaryPrimitives.WriteInt32BigEndian(buffer, value);

        this.BaseStream.Write(buffer);
    }

    private void WriteFloatInternal(float value)
    {
        Span<byte> buffer = stackalloc byte[4];

        BinaryPrimitives.WriteSingleBigEndian(buffer, value);

        this.BaseStream.Write(buffer);
    }

    private void WriteLongInternal(long value)
    {
        Span<byte> buffer = stackalloc byte[8];

        BinaryPrimitives.WriteInt64BigEndian(buffer, value);

        this.BaseStream.Write(buffer);
    }

    private void WriteDoubleInternal(double value)
    {
        Span<byte> buffer = stackalloc byte[8];

        BinaryPrimitives.WriteDoubleBigEndian(buffer, value);

        this.BaseStream.Write(buffer);
    }
}
