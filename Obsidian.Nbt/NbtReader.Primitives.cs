using System.Buffers.Binary;
using System.Text;

namespace Obsidian.Nbt;
public partial struct NbtReader
{
    public byte ReadByte() => (byte)this.BaseStream.ReadByte();

    public string ReadString()
    {
        var length = this.ReadInt16();

        if (length <= 0)
            return string.Empty;

        Span<byte> buffer = stackalloc byte[length];

        this.BaseStream.Read(buffer);

        return Encoding.UTF8.GetString(buffer);
    }

    public short ReadInt16()
    {
        Span<byte> buffer = stackalloc byte[2];
        this.BaseStream.Read(buffer);

        return BinaryPrimitives.ReadInt16BigEndian(buffer);
    }

    public int ReadInt32()
    {
        Span<byte> buffer = stackalloc byte[4];
        this.BaseStream.Read(buffer);

        return BinaryPrimitives.ReadInt32BigEndian(buffer);
    }

    public long ReadInt64()
    {
        Span<byte> buffer = stackalloc byte[8];
        this.BaseStream.Read(buffer);

        return BinaryPrimitives.ReadInt64BigEndian(buffer);
    }

    public float ReadSingle()
    {
        Span<byte> buffer = stackalloc byte[4];
        this.BaseStream.Read(buffer);

        return BinaryPrimitives.ReadSingleBigEndian(buffer);
    }

    public double ReadDouble()
    {
        Span<byte> buffer = stackalloc byte[8];
        this.BaseStream.Read(buffer);

        return BinaryPrimitives.ReadDoubleBigEndian(buffer);
    }
}
