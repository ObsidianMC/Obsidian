using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Obsidian.Nbt;

public sealed class NbtReader(Stream input, NbtCompression compressionMode = NbtCompression.None)
{
    public NbtTagType? CurrentTag { get; set; }

    public Stream BaseStream { get; } = compressionMode switch
    {
        NbtCompression.GZip => new GZipStream(input, CompressionMode.Decompress),
        NbtCompression.ZLib => new ZLibStream(input, CompressionMode.Decompress),
        _ => input
    };

    public NbtTagType ReadTagType()
    {
        var type = this.BaseStream.ReadByte();

        return type switch
        {
            <= 0 => NbtTagType.End,
            > (byte)NbtTagType.LongArray => throw new ArgumentOutOfRangeException(
                $"Tag is out of range: {(NbtTagType)type}"),
            _ => (NbtTagType)type
        };
    }

    private INbtTag GetCurrentTag(NbtTagType type, bool readName = true)
    {
        string name = readName ? this.ReadString() : string.Empty;

        INbtTag tag = type switch
        {
            NbtTagType.Byte => new NbtTag<byte>(name, this.ReadByte()),
            NbtTagType.Short => new NbtTag<short>(name, this.ReadInt16()),
            NbtTagType.Int => new NbtTag<int>(name, this.ReadInt32()),
            NbtTagType.Long => new NbtTag<long>(name, this.ReadInt64()),
            NbtTagType.Float => new NbtTag<float>(name, this.ReadSingle()),
            NbtTagType.Double => new NbtTag<double>(name, this.ReadDouble()),
            NbtTagType.String => new NbtTag<string>(name, this.ReadString()),
            NbtTagType.Compound => this.ReadCompoundTag(name),
            NbtTagType.List => this.ReadListTag(name),
            NbtTagType.ByteArray => this.ReadArray(name, ReadByte),
            NbtTagType.IntArray => this.ReadArray(name, ReadInt32),
            NbtTagType.LongArray => this.ReadArray(name, ReadInt64),
            _ => null
        };

        return tag;
    }

    private INbtTag GetCurrentTag(NbtTagType type, string name, bool readName = true)
    {
        name = readName ? this.ReadString() : name;

        INbtTag tag = type switch
        {
            NbtTagType.Byte => new NbtTag<byte>(name, this.ReadByte()),
            NbtTagType.Short => new NbtTag<short>(name, this.ReadInt16()),
            NbtTagType.Int => new NbtTag<int>(name, this.ReadInt32()),
            NbtTagType.Long => new NbtTag<long>(name, this.ReadInt64()),
            NbtTagType.Float => new NbtTag<float>(name, this.ReadSingle()),
            NbtTagType.Double => new NbtTag<double>(name, this.ReadDouble()),
            NbtTagType.String => new NbtTag<string>(name, this.ReadString()),
            NbtTagType.Compound => this.ReadCompoundTag(name),
            NbtTagType.List => this.ReadListTag(name),
            NbtTagType.ByteArray => this.ReadArray(name, ReadByte),
            NbtTagType.IntArray => this.ReadArray(name, ReadInt32),
            NbtTagType.LongArray => this.ReadArray(name, ReadInt64),
            _ => null
        };

        return tag;
    }

    private INbtTag ReadArray<T>(string name, Func<T> readElement) where T : struct
    {
        int length = ReadInt32();
        if (length < 0)
            throw new UnreachableException("Array length should never be below 0.");

        var array = new T[length];
        for (int i = 0; i < length; i++)
        {
            array[i] = readElement();
        }

        return new NbtArray<T>(name, array);
    }

    private NbtList ReadListTag(string name)
    {
        var listType = this.ReadTagType();

        var length = this.ReadInt32();

        if (length <= 0)
            return new NbtList(listType, name);

        var list = new NbtList(listType, name);
        for (var i = 0; i < length; i++)
            list.Add(this.GetCurrentTag(listType, false));

        return list;
    }

    private NbtCompound ReadCompoundTag(string name)
    {
        var compound = new NbtCompound(name);

        NbtTagType type;
        while ((type = this.ReadTagType()) != NbtTagType.End)
        {
            var tag = this.GetCurrentTag(type);

            compound.Add(tag);
        }

        return compound;
    }

    public INbtTag? ReadNextTag(bool readName = true)
    {
        var firstType = this.ReadTagType();

        string tagName = readName ? this.ReadString() : string.Empty;

        return firstType switch
        {
            NbtTagType.End => null,
            NbtTagType.List => ReadListTag(tagName),
            NbtTagType.Compound => ReadCompoundTag(tagName),
            NbtTagType.ByteArray => ReadArray(tagName, ReadByte),
            NbtTagType.IntArray => ReadArray(tagName, ReadInt32),
            NbtTagType.LongArray => ReadArray(tagName, ReadInt64),
            _ => GetCurrentTag(firstType, tagName, !readName)
        };
    }

    #region Methods

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
    #endregion
}
