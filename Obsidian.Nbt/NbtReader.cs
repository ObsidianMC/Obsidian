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
        if (compressionMode == NbtCompression.GZip)
            BaseStream = new GZipStream(input, CompressionMode.Decompress);
        else if (compressionMode == NbtCompression.ZLib)
            BaseStream = new ZLibStream(input, CompressionMode.Decompress);
        else
            BaseStream = input;
    }

    public NbtTagType ReadTagType()
    {
        var type = BaseStream.ReadByte();

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
            name = ReadString();

        INbtTag tag = type switch
        {
            NbtTagType.Byte => new NbtTag<byte>(name, ReadByte()),
            NbtTagType.Short => new NbtTag<short>(name, ReadInt16()),
            NbtTagType.Int => new NbtTag<int>(name, ReadInt32()),
            NbtTagType.Long => new NbtTag<long>(name, ReadInt64()),
            NbtTagType.Float => new NbtTag<float>(name, ReadSingle()),
            NbtTagType.Double => new NbtTag<double>(name, ReadDouble()),
            NbtTagType.String => new NbtTag<string>(name, ReadString()),
            NbtTagType.Compound => ReadCompoundTag(name),
            NbtTagType.List => ReadListTag(name),
            NbtTagType.ByteArray => ReadArray(name, type),
            NbtTagType.IntArray => ReadArray(name, type),
            NbtTagType.LongArray => ReadArray(name, type),
            _ => null
        };

        return tag;
    }

    private INbtTag ReadArray(string name, NbtTagType type)
    {
        var length = ReadInt32();

        switch (type)
        {
            case NbtTagType.ByteArray:
                {
                    var buffer = new byte[length];

                    BaseStream.Read(buffer);

                    return new NbtArray<byte>(name, buffer);
                }
            case NbtTagType.IntArray:
                {
                    var array = new NbtArray<int>(name, length);

                    for (int i = 0; i < array.Count; i++)
                    {
                        array[i] = ReadInt32();
                    }
                    return array;
                }
            case NbtTagType.LongArray:
                {
                    var array = new NbtArray<long>(name, length);

                    for (int i = 0; i < array.Count; i++)
                        array[i] = ReadInt64();

                    return array;
                }
            default:
                throw new InvalidOperationException();
        }
    }

    private NbtList ReadListTag(string name)
    {
        var listType = ReadTagType();

        var list = new NbtList(listType, name);

        var length = ReadInt32();

        if (length < 0)
            throw new InvalidOperationException("Got negative list length.");

        for (var i = 0; i < length; i++)
            list.Add(GetCurrentTag(listType, false));

        return list;
    }

    private NbtCompound ReadCompoundTag(string name)
    {
        var compound = new NbtCompound(name);

        NbtTagType type;
        while ((type = ReadTagType()) != NbtTagType.End)
        {
            var tag = GetCurrentTag(type);

            compound.Add(tag);
        }

        return compound;
    }

    public INbtTag ReadNextTag(bool readName = true)
    {
        var firstType = ReadTagType();

        string tagName = "";

        if (readName)
            tagName = ReadString();

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
                    var tag = GetCurrentTag(firstType, !readName);

                    if (readName)
                        tag.Name = tagName;

                    return tag;
                }
            case NbtTagType.List:
                var listType = ReadTagType();

                var list = new NbtList(listType, tagName);

                var length = ReadInt32();

                if (length < 0)
                    throw new InvalidOperationException("Got negative list length.");

                for (var i = 0; i < length; i++)
                    list.Add(GetCurrentTag(listType, false));

                return list;
            case NbtTagType.Compound:
                return ReadCompoundTag(tagName);
            case NbtTagType.ByteArray:
                return ReadArray(tagName, firstType);
            case NbtTagType.IntArray:
                return ReadArray(tagName, firstType);
            case NbtTagType.LongArray:
                return ReadArray(tagName, firstType);
            case NbtTagType.Unknown:
                break;
            default:
                break;
        }

        return null;
    }

    #region Methods

    public byte ReadByte() => (byte)BaseStream.ReadByte();

    public string ReadString()
    {
        var length = ReadInt16();

        if (length <= 0)
            return null;

        Span<byte> buffer = stackalloc byte[length];

        BaseStream.Read(buffer);

        return Encoding.UTF8.GetString(buffer);
    }

    public short ReadInt16()
    {
        Span<byte> buffer = stackalloc byte[2];
        BaseStream.Read(buffer);

        return BinaryPrimitives.ReadInt16BigEndian(buffer);
    }

    public int ReadInt32()
    {
        Span<byte> buffer = stackalloc byte[4];
        BaseStream.Read(buffer);

        return BinaryPrimitives.ReadInt32BigEndian(buffer);
    }

    public long ReadInt64()
    {
        Span<byte> buffer = stackalloc byte[8];
        BaseStream.Read(buffer);

        return BinaryPrimitives.ReadInt64BigEndian(buffer);
    }

    public float ReadSingle()
    {
        Span<byte> buffer = stackalloc byte[4];
        BaseStream.Read(buffer);

        return BinaryPrimitives.ReadSingleBigEndian(buffer);
    }

    public double ReadDouble()
    {
        Span<byte> buffer = stackalloc byte[8];
        BaseStream.Read(buffer);

        return BinaryPrimitives.ReadDoubleBigEndian(buffer);
    }
    #endregion
}
