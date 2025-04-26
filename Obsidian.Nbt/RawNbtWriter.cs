using Obsidian.Nbt.Interfaces;
using Obsidian.Nbt.Utilities;
using System.Buffers;

namespace Obsidian.Nbt;
public sealed partial class RawNbtWriter : INbtWriter
{
    private const int MaxBufferSize = 1024 * 4;

    private bool disposed;

    private byte[] data;
    private NbtWriterState? currentState;
    private int offset;

    public NbtTagType? RootType { get; private set; }

    public bool Networked { get; }

    public Span<byte> Data => this.AsSpan();

    public int Offset => this.offset;

    public RawNbtWriter(string name)
    {
        this.data = ArrayPool<byte>.Shared.Rent(MaxBufferSize);

        this.Write(NbtTagType.Compound);
        this.Write(name);

        this.SetRootTag(NbtTagType.Compound);
    }

    public RawNbtWriter(bool networked)
    {
        this.data = ArrayPool<byte>.Shared.Rent(MaxBufferSize);
        this.Networked = networked;

        this.Write(NbtTagType.Compound);
        this.SetRootTag(NbtTagType.Compound);
    }

    public void WriteArray(string? name, ReadOnlySpan<int> values)
    {
        this.Write(NbtTagType.IntArray);
        this.Write(name);
        this.Write(values.Length);

        for (int i = 0; i < values.Length; i++)
            this.Write(values[i]);
    }

    public void WriteArray(string? name, ReadOnlySpan<long> values)
    {
        this.Write(NbtTagType.LongArray);
        this.Write(name);
        this.Write(values.Length);

        for (int i = 0; i < values.Length; i++)
            this.Write(values[i]);
    }

    public void WriteArray(string? name, ReadOnlySpan<byte> values)
    {
        this.Write(NbtTagType.ByteArray);
        this.Write(name);
        this.Write(values.Length);

        this.Write(values);
    }

    public void WriteCompoundStart(string name = "")
    {
        this.Validate(name, NbtTagType.Compound);

        this.SetRootTag(NbtTagType.Compound);
        if (this.RootType == NbtTagType.List)
            return;

        this.Write(NbtTagType.Compound);
        this.Write(name);
    }

    public void WriteListStart(string name, NbtTagType listType, int length, bool writeName = true)
    {
        this.Validate(name, NbtTagType.List);

        this.SetRootTag(NbtTagType.List, length, listType);

        this.Write(NbtTagType.List);

        if (writeName)
            this.Write(name);

        this.Write(listType);
        this.Write(length);
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

    public void EndList()
    {
        if (this.currentState!.ListIndex < this.currentState?.ListSize)
            throw new InvalidOperationException("List cannot end because its size is smaller than the pre-defined size.");

        if (this.RootType != NbtTagType.List)
            throw new InvalidOperationException();

        this.RootType = this.currentState?.ParentTagType ?? NbtTagType.End;

        this.currentState = this.currentState.PreviousState;
    }

    public void TryFinish()
    {
        if (this.currentState != null)
            throw new InvalidOperationException($"Unable to close writer. Root tag has yet to be closed.");//TODO maybe more info here??
    }

    public Task TryFinishAsync()
    {
        this.TryFinish();
        return Task.CompletedTask;
    }

    public Span<byte> AsSpan() => new(data, 0, offset);

    public void Dispose()
    {
        if (this.disposed)
            throw new ObjectDisposedException(nameof(RawNbtWriter));

        this.disposed = true;

        ArrayPool<byte>.Shared.Return(this.data);
    }

    public ValueTask DisposeAsync()
    {
        this.Dispose();

        return default;
    }

    public void Write(NbtTagType tagType) => this.Write((byte)tagType);

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
}
