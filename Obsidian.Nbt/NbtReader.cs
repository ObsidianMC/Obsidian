using ImpromptuNinjas.ZStd;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Obsidian.Nbt;

public class NbtReader
{
    public NbtTagType? CurrentTag { get; set; }

    public Stream BaseStream { get; }

    public NbtReader() : this(new MemoryStream()) { }

    public NbtReader(Stream input, NbtCompression compressionMode = NbtCompression.None)
    {
        switch (compressionMode)
        {
            case NbtCompression.None:
                this.BaseStream = input;
                break;
            case NbtCompression.GZip:
                this.BaseStream = new GZipStream(input, CompressionMode.Decompress);
                break;
            case NbtCompression.ZLib:
                this.BaseStream = new ZLibStream(input, CompressionMode.Decompress);
                break;
            case NbtCompression.Auto:
            case NbtCompression.Zstd:
                this.BaseStream = new ZStdDecompressStream(BaseStream);
                break;
        }
    }

    public NbtTagType ReadTagType()
    {
        var type = this.BaseStream.ReadByte();

        if (type <= 0)
            return NbtTagType.End;
        else if (type > (byte)NbtTagType.LongArray)
            throw new ArgumentOutOfRangeException($"Tag is out of range: {(NbtTagType)type}");

        return (NbtTagType)type;
    }

    private INbtTag GetCurrentTag(NbtTagType type, bool readName = true)
    {
        string name = string.Empty;

        if (readName)
            name = this.ReadString();

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
            NbtTagType.ByteArray => this.ReadArray(name, type),
            NbtTagType.IntArray => this.ReadArray(name, type),
            NbtTagType.LongArray => this.ReadArray(name, type),
            _ => null
        };

        return tag;
    }

    private INbtTag ReadArray(string name, NbtTagType type)
    {
        var length = this.ReadInt32();

        switch (type)
        {
            case NbtTagType.ByteArray:
                {
                    var buffer = new byte[length];

                    this.BaseStream.Read(buffer);

                    return new NbtArray<byte>(name, buffer);
                }
            case NbtTagType.IntArray:
                {
                    var array = new NbtArray<int>(name, length);

                    for (int i = 0; i < array.Count; i++)
                    {
                        array[i] = this.ReadInt32();
                    }
                    return array;
                }
            case NbtTagType.LongArray:
                {
                    var array = new NbtArray<long>(name, length);

                    for (int i = 0; i < array.Count; i++)
                        array[i] = this.ReadInt64();

                    return array;
                }
            default:
                throw new InvalidOperationException();
        }
    }

    private NbtList ReadListTag(string name)
    {
        var listType = this.ReadTagType();

        var list = new NbtList(listType, name);

        var length = this.ReadInt32();

        if (length < 0)
            throw new InvalidOperationException("Got negative list length.");

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

    public INbtTag ReadNextTag(bool readName = true)
    {
        var firstType = this.ReadTagType();

        string tagName = "";

        if (readName)
            tagName = this.ReadString();

        switch (firstType)
        {
            case NbtTagType.End:
                return null;
            case NbtTagType.Byte:
            case NbtTagType.Short:
            case NbtTagType.Int:
            case NbtTagType.Long:
            case NbtTagType.Float:
            case NbtTagType.Double:
            case NbtTagType.String:
                {
                    var tag = this.GetCurrentTag(firstType, !readName);

                    if (readName)
                        tag.Name = tagName;

                    return tag;
                }
            case NbtTagType.List:
                var listType = this.ReadTagType();

                var list = new NbtList(listType, tagName);

                var length = this.ReadInt32();

                if (length < 0)
                    throw new InvalidOperationException("Got negative list length.");

                for (var i = 0; i < length; i++)
                    list.Add(this.GetCurrentTag(listType, false));

                return list;
            case NbtTagType.Compound:
                return this.ReadCompoundTag(tagName);
            case NbtTagType.ByteArray:
                return this.ReadArray(tagName, firstType);
            case NbtTagType.IntArray:
                return this.ReadArray(tagName, firstType);
            case NbtTagType.LongArray:
                return this.ReadArray(tagName, firstType);
            case NbtTagType.Unknown:
                break;
            default:
                break;
        }

        return null;
    }

    #region Methods

    public byte ReadByte() => (byte)this.BaseStream.ReadByte();

    public string ReadString()
    {
        var length = this.ReadInt16();

        if (length <= 0)
            return null;

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
