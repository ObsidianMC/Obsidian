﻿using System.IO;
using System.IO.Compression;

namespace Obsidian.Nbt;

public partial struct NbtWriter(Stream outstream, NbtCompression compressionMode = NbtCompression.None) : IDisposable, IAsyncDisposable
{
    private ListData? currentListData;
    private NbtTagType? previousRootType;
    private List<string> compoundChildren = [];

    internal int rootIndex = 0;

    public NbtTagType RootType { get; private set; }

    public Stream BaseStream { get; } = compressionMode switch
    {
        NbtCompression.GZip => new GZipStream(outstream, CompressionMode.Compress),
        NbtCompression.ZLib => new ZLibStream(outstream, CompressionMode.Compress),
        _ => outstream
    };

    public bool Networked { get; }

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

    public NbtWriter(Stream outstream, NbtCompression compressionMode, string name) : this(outstream, compressionMode)
    {
        this.Write(NbtTagType.Compound);
        this.WriteStringInternal(name);

        this.SetRootTag(NbtTagType.Compound);
    }

    private void SetRootTag(NbtTagType type, bool addRoot = true)
    {
        if (addRoot)
            this.rootIndex++;

        this.previousRootType = this.RootType;
        this.RootType = type;
    }

    private void SetRootTag(NbtTagType type, int listSize, NbtTagType listType, bool addRoot = true)
    {
        if (addRoot)
            this.rootIndex++;

        var previousListData = this.currentListData;
        this.currentListData = new()
        {
            RootIndex = this.rootIndex,
            ExpectedListType = listType,
            ListSize = listSize,
            ListIndex = 0,
            PreviousListData = previousListData,
            ParentTagType = this.RootType
        };

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
        if (this.currentListData!.ListIndex < this.currentListData?.ListSize)
            throw new InvalidOperationException("List cannot end because its size is smaller than the pre-defined size.");

        if (this.RootType != NbtTagType.List)
            throw new InvalidOperationException();

        this.currentListData = this.currentListData.PreviousListData;
        this.rootIndex--;

        this.RootType = this.currentListData?.ParentTagType ?? this.previousRootType ?? NbtTagType.End;

        if (this.previousRootType != null)
            this.previousRootType = null;
    }

    public void EndCompound()
    {
        if (this.RootType != NbtTagType.Compound)
            throw new InvalidOperationException();

        this.compoundChildren.Clear();
        this.rootIndex--;
        if (this.currentListData != null)
        {
            var tagType = this.currentListData.RootIndex == this.rootIndex ? NbtTagType.List : this.currentListData.ParentTagType ?? NbtTagType.End;
            this.SetRootTag(tagType, false);
            this.Write(NbtTagType.End);

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

    public void Validate(string name, NbtTagType type)
    {
        this.TryValidateList(name, type);

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException($"Tags inside a compound tag must have a name. Tag({type})");

        if (this.compoundChildren.Contains(name))
            throw new ArgumentException($"Tag with name {name} already exists.");

        this.compoundChildren.Add(name);
    }

    private void TryValidateList(string name, NbtTagType type)
    {
        if (this.RootType != NbtTagType.List)
            return;

        if (!string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Tags inside lists cannot be named.");

        if (this.currentListData!.ExpectedListType != type)
            throw new InvalidOperationException($"Expected list type: {this.currentListData!.ExpectedListType}. Got: {type}");
        else if (!string.IsNullOrEmpty(name))
            throw new InvalidOperationException("Tags inside lists must be nameless.");
        else if (this.currentListData!.ListIndex > this.currentListData!.ListSize)
            throw new IndexOutOfRangeException("Exceeded pre-defined list size");

        this.currentListData!.ListIndex++;
    }

    public void TryFinish()
    {
        if (this.rootIndex > 0)
            throw new InvalidOperationException($"Unable to close writer. Root tag has yet to be closed. rootDept == {this.rootIndex}");//TODO maybe more info here??

        this.BaseStream.Flush();
    }

    public async Task TryFinishAsync()
    {
        if (this.rootIndex > 0)
            throw new InvalidOperationException("Unable to close writer. Root tag has yet to be closed.");//TODO maybe more info here??

        await this.BaseStream.FlushAsync();
    }

    public ValueTask DisposeAsync() => this.BaseStream.DisposeAsync();
    public void Dispose() => this.BaseStream.Dispose();

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

    private sealed class ListData
    {
        public required int RootIndex { get; init; }

        public required int ListSize { get; init; }

        public required int ListIndex { get; set; }

        public required NbtTagType ExpectedListType { get; init; }

        public required ListData? PreviousListData { get; init; }

        public required NbtTagType? ParentTagType { get; init; }
    }
}
