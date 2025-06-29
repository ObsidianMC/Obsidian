using Obsidian.Nbt.Interfaces;
using Obsidian.Nbt.Utilities;
using System.IO;
using System.IO.Compression;

namespace Obsidian.Nbt;

public partial struct NbtWriterStream(Stream outstream, NbtCompression compressionMode = NbtCompression.None) : INbtWriter
{
    private NbtWriterState? currentState;
    public NbtTagType? RootType { get; private set; }

    public Stream BaseStream { get; } = compressionMode switch
    {
        NbtCompression.GZip => new GZipStream(outstream, CompressionMode.Compress),
        NbtCompression.ZLib => new ZLibStream(outstream, CompressionMode.Compress),
        _ => outstream
    };

    public bool Networked { get; }

    public NbtWriterStream(Stream outstream, string name) : this(outstream)
    {
        this.Write(NbtTagType.Compound);
        this.WriteStringInternal(name);

        this.SetRootTag(NbtTagType.Compound);
    }

    public NbtWriterStream(Stream outstream, bool networked) : this(outstream)
    {
        this.Networked = networked;

        this.Write(NbtTagType.Compound);

        this.SetRootTag(NbtTagType.Compound);
    }

    public NbtWriterStream(Stream outstream, NbtCompression compressionMode, string name) : this(outstream, compressionMode)
    {
        this.Write(NbtTagType.Compound);
        this.WriteStringInternal(name);

        this.SetRootTag(NbtTagType.Compound);
    }

    private void SetRootTag(NbtTagType type, bool addRoot = true)
    {
        if (addRoot)
        {
            this.currentState = new()
            {
                PreviousState = this.currentState,
                ExpectedListType = null,
                ParentTagType = this.RootType ?? type,
                ChildrenAdded = []
            };
        }

        this.RootType = type;
    }

    private void SetRootTag(NbtTagType type, int listSize, NbtTagType listType, bool addRoot = true)
    {
        if (addRoot)
        {
            this.currentState = new()
            {
                ExpectedListType = listType,
                ListSize = listSize,
                ListIndex = 0,
                PreviousState = this.currentState,
                ParentTagType = this.RootType ?? type
            };
        }

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
        if (this.currentState!.ListIndex < this.currentState?.ListSize)
            throw new InvalidOperationException("List cannot end because its size is smaller than the pre-defined size.");

        if (this.RootType != NbtTagType.List)
            throw new InvalidOperationException();

        this.RootType = this.currentState?.ParentTagType ?? NbtTagType.End;

        this.currentState = this.currentState.PreviousState;
    }

    public void EndCompound()
    {
        if (this.RootType != NbtTagType.Compound)
            throw new InvalidOperationException();

        this.RootType = this.currentState?.ParentTagType ?? NbtTagType.End;
        this.currentState = this.currentState.PreviousState;

        if (this.currentState != null && this.currentState.ExpectedListType != null)
        {
            this.SetRootTag(NbtTagType.List, false);
            this.Write(NbtTagType.End);

            return;
        }

        this.Write(NbtTagType.End);
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

    public void WriteArray(string? name, ReadOnlySpan<int> values)
    {
        this.Write(NbtTagType.IntArray);
        this.WriteStringInternal(name);
        this.WriteIntInternal(values.Length);

        for (int i = 0; i < values.Length; i++)
            this.WriteIntInternal(values[i]);
    }

    public void WriteArray(string? name, ReadOnlySpan<long> values)
    {
        this.Write(NbtTagType.LongArray);
        this.WriteStringInternal(name);
        this.WriteIntInternal(values.Length);

        for (int i = 0; i < values.Length; i++)
            this.WriteLongInternal(values[i]);
    }

    public void WriteArray(string? name, ReadOnlySpan<byte> values)
    {
        this.Write(NbtTagType.ByteArray);
        this.WriteStringInternal(name);
        this.WriteIntInternal(values.Length);

        this.BaseStream.Write(values);
    }

    private void Validate(string name, NbtTagType type)
    {
        if (this.TryValidateList(name, type))
            return;

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException($"Tags inside a compound tag must have a name. Tag({type})");

        if (this.currentState.ChildrenAdded.Contains(name))
            throw new ArgumentException($"Tag with name {name} already exists.");

        this.currentState.ChildrenAdded.Add(name);
    }

    private bool TryValidateList(string name, NbtTagType type)
    {
        if (this.RootType != NbtTagType.List)
            return false;

        if (!string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Tags inside lists cannot be named.");

        if (!this.currentState!.HasExpectedListType(type))
            throw new InvalidOperationException($"Expected list type: {this.currentState!.ExpectedListType}. Got: {type}");
        else if (!string.IsNullOrEmpty(name))
            throw new InvalidOperationException("Tags inside lists must be nameless.");
        else if (this.currentState!.ListIndex > this.currentState!.ListSize)
            throw new IndexOutOfRangeException("Exceeded pre-defined list size");

        this.currentState!.ListIndex++;

        return true;
    }

    public readonly void TryFinish()
    {
        if (this.currentState != null)
            throw new InvalidOperationException($"Unable to close writer. Root tag has yet to be closed.");//TODO maybe more info here??

        this.BaseStream.Flush();
    }

    public readonly async Task TryFinishAsync()
    {
        if (this.currentState != null)
            throw new InvalidOperationException("Unable to close writer. Root tag has yet to be closed.");//TODO maybe more info here??

        await this.BaseStream.FlushAsync();
    }

    public readonly ValueTask DisposeAsync() => this.BaseStream.DisposeAsync();
    public readonly void Dispose() => this.BaseStream.Dispose();

    private void WriteArray(INbtTag array)
    {
        this.Validate(array.Name, array.Type);

        if (array is NbtArray<int> intArray)
        {
            this.WriteArray(intArray.Name, intArray.GetArray());
        }
        else if (array is NbtArray<long> longArray)
        {
            this.WriteArray(longArray.Name, longArray.GetArray());
        }
        else if (array is NbtArray<byte> byteArray)
        {
            this.WriteArray(byteArray.Name, byteArray.GetArray());
        }
    }
}
