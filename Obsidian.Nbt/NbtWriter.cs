using System.IO;
using System.IO.Compression;

namespace Obsidian.Nbt;

public partial struct NbtWriter : IDisposable, IAsyncDisposable
{
    private NbtTagType? expectedListType;
    private NbtTagType? previousRootType;

    private int listSize;
    private int listIndex;
    private int rootDepth;

    public NbtTagType RootType { get; private set; }

    public Stream BaseStream { get; }

    public bool Networked { get; }

    public NbtWriter(Stream outstream, NbtCompression compressionMode = NbtCompression.None)
    {
        this.BaseStream = compressionMode switch
        {
            NbtCompression.GZip => new GZipStream(outstream, CompressionMode.Compress),
            NbtCompression.ZLib => new ZLibStream(outstream, CompressionMode.Compress),
            _ => outstream
        };

        this.expectedListType = null;
        this.previousRootType = null;

        this.listSize = -1;
        this.listIndex = -1;
        this.rootDepth = 0;
    }

    public NbtWriter(Stream outstream, string name) : this(outstream)
    {
        this.Write(NbtTagType.Compound);
        this.WriteStringInternal(name);

        this.SetRootTag(NbtTagType.Compound);
    }

    public NbtWriter(Stream outstream, bool networked) : this(outstream)
    {
        this.Networked = networked;

        this.Write(NbtTagType.Compound);

        this.SetRootTag(NbtTagType.Compound);
    }

    public NbtWriter(Stream outstream, NbtCompression compressionMode, string name)
    {
        this.BaseStream = compressionMode switch
        {
            NbtCompression.GZip => new GZipStream(outstream, CompressionMode.Compress),
            NbtCompression.ZLib => new ZLibStream(outstream, CompressionMode.Compress),
            _ => outstream
        };

        this.Write(NbtTagType.Compound);
        this.WriteStringInternal(name);

        this.SetRootTag(NbtTagType.Compound);
    }

    private void SetRootTag(NbtTagType type)
    {
        this.rootDepth++;

        this.previousRootType = this.RootType;
        this.RootType = type;
    }

    private void SetRootTag(NbtTagType type, int listSize, NbtTagType listType)
    {
        this.rootDepth++;

        this.listIndex = 0;
        this.listSize = listSize;
        this.expectedListType = listType;

        this.previousRootType = this.RootType;
        this.RootType = type;
    }

    public void WriteCompoundStart(string name = "")
    {
        this.Validate(name, NbtTagType.Compound);

        //Lists don't write tag type or tag name for its children
        if (this.RootType == NbtTagType.List)
        {
            this.SetRootTag(NbtTagType.Compound);
            return;
        }

        this.SetRootTag(NbtTagType.Compound);

        this.Write(NbtTagType.Compound);
        this.WriteStringInternal(name);
    }

    public void WriteListStart(string name, NbtTagType listType, int length, bool writeName = true)
    {
        this.Validate(name, NbtTagType.List);

        this.SetRootTag(NbtTagType.List, length, listType);

        this.Write(NbtTagType.List);

        if (writeName)
            this.WriteStringInternal(name);

        this.Write(listType);
        this.WriteIntInternal(length);
    }

    public void EndList()
    {
        if (this.listIndex < this.listSize)
            throw new InvalidOperationException("List cannot end because its size is smaller than the pre-defined size.");

        if (this.RootType != NbtTagType.List)
            throw new InvalidOperationException();

        this.listSize = -1;
        this.listIndex = -1;
        this.expectedListType = null;
        this.rootDepth--;

        this.RootType = this.previousRootType ?? NbtTagType.End;

        if (this.previousRootType != null)
            this.previousRootType = null;
    }

    public void EndCompound()
    {
        if (this.RootType != NbtTagType.Compound)
            throw new InvalidOperationException();

        this.rootDepth--;
        if (this.expectedListType != null)
        {
            this.SetRootTag(NbtTagType.List);

            return;
        }

        this.Write(NbtTagType.End);
        this.RootType = this.previousRootType ?? NbtTagType.End;

        if (this.previousRootType != null)
            this.previousRootType = null;
    }

    public void WriteTag(INbtTag tag)
    {
        var name = tag.Name;

        switch (tag.Type)
        {
            case NbtTagType.End:
                throw new InvalidOperationException("Use writer.EndCompound() instead.");
            case NbtTagType.Byte:
                if (tag is NbtTag<byte> byteTag)
                {
                    this.WriteByte(name, byteTag.Value);
                }
                else if (tag is NbtTag<bool> boolValue)
                {
                    this.WriteByte(name, (byte)(boolValue.Value ? 1 : 0));
                }
                break;
            case NbtTagType.Short:
                this.WriteShort(name, ((NbtTag<short>)tag).Value);
                break;
            case NbtTagType.Int:
                this.WriteInt(name, ((NbtTag<int>)tag).Value);
                break;
            case NbtTagType.Long:
                this.WriteLong(name, ((NbtTag<long>)tag).Value);
                break;
            case NbtTagType.Float:
                this.WriteFloat(name, ((NbtTag<float>)tag).Value);
                break;
            case NbtTagType.Double:
                this.WriteDouble(name, ((NbtTag<double>)tag).Value);
                break;
            case NbtTagType.String:
                this.WriteString(name, ((NbtTag<string>)tag).Value);
                break;
            case NbtTagType.List:
                var list = (NbtList)tag;

                this.WriteListStart(name, list.ListType, list.Count);

                foreach (var child in list)
                    this.WriteListTag(child);

                this.EndList();
                break;
            case NbtTagType.Compound:
                this.WriteCompoundStart(name);

                foreach (var (_, child) in (NbtCompound)tag)
                    this.WriteTag(child);

                this.EndCompound();
                break;
            case NbtTagType.ByteArray:
            case NbtTagType.IntArray:
            case NbtTagType.LongArray:
                this.WriteArray(tag);
                break;
            case NbtTagType.Unknown:
            default:
                throw new InvalidOperationException("Unknown tag type");
        }
    }

    public void WriteListTag(INbtTag tag)
    {
        var name = tag.Name;

        switch (tag.Type)
        {
            case NbtTagType.End:
                throw new InvalidOperationException("Use writer.EndCompound() instead.");
            case NbtTagType.Byte:
                if (tag is NbtTag<byte> byteTag)
                {
                    this.WriteByte(byteTag.Value);
                }
                else if (tag is NbtTag<bool> boolValue)
                {
                    this.WriteByte((byte)(boolValue.Value ? 1 : 0));
                }
                break;
            case NbtTagType.Short:
                this.WriteShort(((NbtTag<short>)tag).Value);
                break;
            case NbtTagType.Int:
                this.WriteInt(((NbtTag<int>)tag).Value);
                break;
            case NbtTagType.Long:
                this.WriteLong(((NbtTag<long>)tag).Value);
                break;
            case NbtTagType.Float:
                this.WriteFloat(((NbtTag<float>)tag).Value);
                break;
            case NbtTagType.Double:
                this.WriteDouble(((NbtTag<double>)tag).Value);
                break;
            case NbtTagType.String:
                this.WriteString(((NbtTag<string>)tag).Value);
                break;
            case NbtTagType.List:
                var list = (NbtList)tag;

                this.WriteListStart(name, list.ListType, list.Count, false);

                foreach (var child in list)
                    this.WriteListTag(child);

                this.EndList();
                break;
            case NbtTagType.Compound:
                this.WriteCompoundStart();

                foreach (var (_, child) in (NbtCompound)tag)
                    this.WriteTag(child);

                this.EndCompound();
                break;
            case NbtTagType.ByteArray:
            case NbtTagType.IntArray:
            case NbtTagType.LongArray:
                this.WriteArray(tag);
                break;
            case NbtTagType.Unknown:
            default:
                throw new InvalidOperationException("Unknown tag type");
        }
    }

    public void WriteArray(INbtTag array)
    {
        this.Validate(array.Name, array.Type);

        if (array is NbtArray<int> intArray)
        {
            this.Write(NbtTagType.IntArray);
            this.WriteStringInternal(array.Name);
            this.WriteIntInternal(intArray.Count);

            for (int i = 0; i < intArray.Count; i++)
                this.WriteIntInternal(intArray[i]);
        }
        else if (array is NbtArray<long> longArray)
        {
            this.Write(NbtTagType.LongArray);
            this.WriteStringInternal(array.Name);
            this.WriteIntInternal(longArray.Count);

            for (int i = 0; i < longArray.Count; i++)
                this.WriteLongInternal(longArray[i]);
        }
        else if (array is NbtArray<byte> byteArray)
        {
            this.Write(NbtTagType.ByteArray);
            this.WriteStringInternal(array.Name);
            this.WriteIntInternal(byteArray.Count);
            this.BaseStream.Write(byteArray.GetArray());
        }
    }

    public void Validate(string name, NbtTagType type)
    {
        if (this.RootType == NbtTagType.List)
        {
            if (!string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException($"Use the Write{type}({type.ToString().ToLower()} value) method when writing to lists");

            if (this.expectedListType != type)
                throw new InvalidOperationException($"Expected list type: {this.expectedListType}. Got: {type}");
            else if (!string.IsNullOrEmpty(name))
                throw new InvalidOperationException("Tags inside lists must be nameless.");
            else if (this.listIndex > this.listSize)
                throw new IndexOutOfRangeException("Exceeded pre-defined list size");

            this.listIndex++;
        }
        else if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException($"Tags inside a compound tag must have a name. Tag({type})");
    }

    public void TryFinish()
    {
        if (this.rootDepth > 0)
            throw new InvalidOperationException("Unable to close writer. Root tag has yet to be closed.");//TODO maybe more info here??

        this.BaseStream.Flush();
    }

    public async Task TryFinishAsync()
    {
        if (this.rootDepth > 0)
            throw new InvalidOperationException("Unable to close writer. Root tag has yet to be closed.");//TODO maybe more info here??

        await this.BaseStream.FlushAsync();
    }

    public ValueTask DisposeAsync() => this.BaseStream.DisposeAsync();
    public void Dispose() => this.BaseStream.Dispose();
}
