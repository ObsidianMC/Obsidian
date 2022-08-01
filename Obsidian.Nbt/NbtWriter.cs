using System.IO;
using System.IO.Compression;

namespace Obsidian.Nbt;

public sealed partial class NbtWriter : IDisposable, IAsyncDisposable
{
    private NbtTagType? expectedListType;

    private Stack<Node> rootNodes = new();

    private int listSize;
    private int listIndex;

    public NbtTagType RootType => this.rootNodes.Count > 0 ? this.rootNodes.Peek().Type : NbtTagType.Unknown;

    public Stream BaseStream { get; }

    public NbtWriter(Stream outstream, NbtCompression compressionMode = NbtCompression.None)
    {
        if (compressionMode == NbtCompression.GZip)
            this.BaseStream = new GZipStream(outstream, CompressionMode.Compress);
        else if (compressionMode == NbtCompression.ZLib)
            this.BaseStream = new ZLibStream(outstream, CompressionMode.Compress);
        else
            this.BaseStream = outstream;
    }

    public NbtWriter(Stream outstream, string name)
    {
        this.BaseStream = outstream;

        this.Write(NbtTagType.Compound);
        this.WriteStringInternal(name);

        this.AddRootTag(new Node { Type = NbtTagType.Compound });
    }

    public NbtWriter(Stream outstream, NbtCompression compressionMode, string name)
    {
        if (compressionMode == NbtCompression.GZip)
            this.BaseStream = new GZipStream(outstream, CompressionMode.Compress);
        else if (compressionMode == NbtCompression.ZLib)
            this.BaseStream = new ZLibStream(outstream, CompressionMode.Compress);
        else
            this.BaseStream = outstream;

        this.Write(NbtTagType.Compound);
        this.WriteStringInternal(name);

        this.AddRootTag(new Node { Type = NbtTagType.Compound });
    }

    private void AddRootTag(Node node)
    {
        if (this.RootType == NbtTagType.List)
        {
            this.rootNodes.Peek().ListIndex = this.listIndex;
            this.listIndex = 0;
        }

        this.rootNodes.Push(node);
    }

    public void WriteCompoundStart(string name = "")
    {
        if (this.rootNodes.Count > 0)
            this.Validate(name, NbtTagType.Compound);

        if (this.RootType == NbtTagType.List)
        {
            this.AddRootTag(new Node { Type = NbtTagType.Compound });
            return;
        }

        this.AddRootTag(new Node { Type = NbtTagType.Compound });

        this.Write(NbtTagType.Compound);
        this.WriteStringInternal(name);
    }

    public void WriteListStart(string name, NbtTagType listType, int length, bool writeName = true)
    {
        this.Validate(name, NbtTagType.List);

        this.AddRootTag(new Node { Type = NbtTagType.List, ListSize = length, ExpectedListType = listType });

        this.listSize = length;
        this.expectedListType = listType;

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

        var tag = this.rootNodes.Pop();
        if (tag.Type != NbtTagType.List)
            throw new InvalidOperationException();

        if (this.CheckIfList())
            return;

        this.listSize = 0;
        this.listIndex = 0;
        this.expectedListType = null;
    }

    public void EndCompound()
    {
        var tag = this.rootNodes.Pop();
        if (tag.Type != NbtTagType.Compound)
            throw new InvalidOperationException();

        this.CheckIfList();

        this.Write(NbtTagType.End);
    }

    private bool CheckIfList()
    {
        if (this.rootNodes.Count <= 0)
            return false;

        var newRoot = this.rootNodes.Peek();

        if (newRoot.Type == NbtTagType.List)
        {
            this.listSize = newRoot.ListSize.Value;
            this.listIndex = newRoot.ListIndex.Value;
            this.expectedListType = newRoot.ExpectedListType.Value;

            return true;
        }

        return false;
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
        if (this.rootNodes.Count > 0)
            throw new InvalidOperationException("Unable to close writer. Some tags have yet to be closed.");//TODO maybe more info here??

        this.BaseStream.Flush();
    }

    public async Task TryFinishAsync()
    {
        if (this.rootNodes.Count > 0)
            throw new InvalidOperationException("Unable to close writer. Some tags have yet to be closed.");//TODO maybe more info here??

        await this.BaseStream.FlushAsync();
    }

    public ValueTask DisposeAsync() => this.BaseStream.DisposeAsync();
    public void Dispose() => this.BaseStream.Dispose();

    private class Node
    {
        public NbtTagType Type { get; set; }

        public int? ListSize { get; set; }

        public int? ListIndex { get; set; }

        public NbtTagType? ExpectedListType { get; set; }
    }
}
