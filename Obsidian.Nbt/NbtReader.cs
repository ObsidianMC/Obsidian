using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;

namespace Obsidian.Nbt;

public readonly partial struct NbtReader(Stream input, NbtCompression compressionMode = NbtCompression.None)
{
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

    public bool TryReadNextTag(bool readName, [MaybeNullWhen(false)] out INbtTag? tag)
    {
        var nextTag = this.ReadNextTag(readName);

        if (nextTag != null)
        {
            tag = nextTag;
            return true;
        }

        tag = default;
        return false;
    }

    public bool TryReadNextTag<T>(bool readName, [MaybeNullWhen(false)] out T? tag) where T : INbtTag
    {
        if (this.TryReadNextTag(readName, out INbtTag? newTag))
        {
            tag = (T)newTag;
            return true;
        }

        tag = default;
        return false;
    }

    public bool TryReadNextTag([MaybeNullWhen(false)] out INbtTag? tag)
    {
        var nextTag = this.ReadNextTag();

        if (nextTag != null)
        {
            tag = nextTag;
            return true;
        }

        tag = default;
        return false;
    }

    public bool TryReadNextTag<T>([MaybeNullWhen(false)] out T? tag) where T : INbtTag
    {
        if (this.TryReadNextTag(out INbtTag? newTag))
        {
            tag = (T)newTag;
            return true;
        }

        tag = default;
        return false;
    }

    private INbtTag? GetCurrentTag(NbtTagType type, bool readName = true)
    {
        string name = readName ? this.ReadString() : string.Empty;

        INbtTag? tag = type switch
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

    private INbtTag? GetCurrentTag(NbtTagType type, string name, bool readName = true)
    {
        name = readName ? this.ReadString() : name;

        INbtTag? tag = type switch
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
}
