using System.IO;
using System.IO.Compression;

namespace Obsidian.Nbt;

public sealed partial class NbtWriter : IDisposable, IAsyncDisposable
{
    private NbtTagType? expectedListType;

    private Stack<Node> rootNodes = new();

    private int listSize;
    private int listIndex;

    public NbtTagType RootType => rootNodes.Count > 0 ? rootNodes.Peek().Type : NbtTagType.Unknown;

    public Stream BaseStream { get; }

    public NbtWriter(Stream outstream, NbtCompression compressionMode = NbtCompression.None)
    {
        if (compressionMode == NbtCompression.GZip)
            BaseStream = new GZipStream(outstream, CompressionMode.Compress);
        else if (compressionMode == NbtCompression.ZLib)
            BaseStream = new ZLibStream(outstream, CompressionMode.Compress);
        else
            BaseStream = outstream;
    }

    public NbtWriter(Stream outstream, string name)
    {
        BaseStream = outstream;

        Write(NbtTagType.Compound);
        WriteStringInternal(name);

        AddRootTag(new Node { Type = NbtTagType.Compound });
    }

    public NbtWriter(Stream outstream, NbtCompression compressionMode, string name)
    {
        if (compressionMode == NbtCompression.GZip)
            BaseStream = new GZipStream(outstream, CompressionMode.Compress);
        else if (compressionMode == NbtCompression.ZLib)
            BaseStream = new ZLibStream(outstream, CompressionMode.Compress);
        else
            BaseStream = outstream;

        Write(NbtTagType.Compound);
        WriteStringInternal(name);

        AddRootTag(new Node { Type = NbtTagType.Compound });
    }

    private void AddRootTag(Node node)
    {
        if (RootType == NbtTagType.List)
        {
            rootNodes.Peek().ListIndex = listIndex;
            listIndex = 0;
        }

        rootNodes.Push(node);
    }

    public void WriteCompoundStart(string name = "")
    {
        if(rootNodes.Count > 0)
            Validate(name, NbtTagType.Compound);

        if (RootType == NbtTagType.List)
        {
            AddRootTag(new Node { Type = NbtTagType.Compound });
            return;
        }

        AddRootTag(new Node { Type = NbtTagType.Compound });

        Write(NbtTagType.Compound);
        WriteStringInternal(name);
    }

    public void WriteListStart(string name, NbtTagType listType, int length, bool writeName = true)
    {
        Validate(name, NbtTagType.List);

        AddRootTag(new Node { Type = NbtTagType.List, ListSize = length, ExpectedListType = listType });

        listSize = length;
        expectedListType = listType;

        Write(NbtTagType.List);

        if (writeName)
            WriteStringInternal(name);

        Write(listType);
        WriteIntInternal(length);
    }

    public void EndList()
    {
        if (listIndex < listSize)
            throw new InvalidOperationException("List cannot end because its size is smaller than the pre-defined size.");

        var tag = rootNodes.Pop();
        if (tag.Type != NbtTagType.List)
            throw new InvalidOperationException();

        if (CheckIfList())
            return;

        listSize = 0;
        listIndex = 0;
        expectedListType = null;
    }

    public void EndCompound()
    {
        var tag = rootNodes.Pop();
        if (tag.Type != NbtTagType.Compound)
            throw new InvalidOperationException();

        CheckIfList();

        Write(NbtTagType.End);
    }

    private bool CheckIfList()
    {
        if (rootNodes.Count <= 0)
            return false;

        var newRoot = rootNodes.Peek();

        if (newRoot.Type == NbtTagType.List)
        {
            listSize = newRoot.ListSize.Value;
            listIndex = newRoot.ListIndex.Value;
            expectedListType = newRoot.ExpectedListType.Value;

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
                    WriteByte(name, byteTag.Value);
                }
                else if (tag is NbtTag<bool> boolValue)
                {
                    WriteByte(name, (byte)(boolValue.Value ? 1 : 0));
                }
                break;
            case NbtTagType.Short:
                WriteShort(name, ((NbtTag<short>)tag).Value);
                break;
            case NbtTagType.Int:
                WriteInt(name, ((NbtTag<int>)tag).Value);
                break;
            case NbtTagType.Long:
                WriteLong(name, ((NbtTag<long>)tag).Value);
                break;
            case NbtTagType.Float:
                WriteFloat(name, ((NbtTag<float>)tag).Value);
                break;
            case NbtTagType.Double:
                WriteDouble(name, ((NbtTag<double>)tag).Value);
                break;
            case NbtTagType.String:
                WriteString(name, ((NbtTag<string>)tag).Value);
                break;
            case NbtTagType.List:
                var list = (NbtList)tag;

                WriteListStart(name, list.ListType, list.Count);

                foreach (var child in list)
                    WriteListTag(child);

                EndList();
                break;
            case NbtTagType.Compound:
                WriteCompoundStart(name);

                foreach (var (_, child) in (NbtCompound)tag)
                    WriteTag(child);

                EndCompound();
                break;
            case NbtTagType.ByteArray:
            case NbtTagType.IntArray:
            case NbtTagType.LongArray:
                WriteArray(tag);
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
                    WriteByte(byteTag.Value);
                }
                else if (tag is NbtTag<bool> boolValue)
                {
                    WriteByte((byte)(boolValue.Value ? 1 : 0));
                }
                break;
            case NbtTagType.Short:
                WriteShort(((NbtTag<short>)tag).Value);
                break;
            case NbtTagType.Int:
                WriteInt(((NbtTag<int>)tag).Value);
                break;
            case NbtTagType.Long:
                WriteLong(((NbtTag<long>)tag).Value);
                break;
            case NbtTagType.Float:
                WriteFloat(((NbtTag<float>)tag).Value);
                break;
            case NbtTagType.Double:
                WriteDouble(((NbtTag<double>)tag).Value);
                break;
            case NbtTagType.String:
                WriteString(((NbtTag<string>)tag).Value);
                break;
            case NbtTagType.List:
                var list = (NbtList)tag;

                WriteListStart(name, list.ListType, list.Count, false);

                foreach (var child in list)
                    WriteListTag(child);

                EndList();
                break;
            case NbtTagType.Compound:
                WriteCompoundStart();

                foreach (var (_, child) in (NbtCompound)tag)
                    WriteTag(child);

                EndCompound();
                break;
            case NbtTagType.ByteArray:
            case NbtTagType.IntArray:
            case NbtTagType.LongArray:
                WriteArray(tag);
                break;
            case NbtTagType.Unknown:
            default:
                throw new InvalidOperationException("Unknown tag type");
        }
    }

    public void WriteArray(INbtTag array)
    {
        Validate(array.Name, array.Type);

        if (array is NbtArray<int> intArray)
        {
            Write(NbtTagType.IntArray);
            WriteStringInternal(array.Name);
            WriteIntInternal(intArray.Count);

            for (int i = 0; i < intArray.Count; i++)
                WriteIntInternal(intArray[i]);
        }
        else if (array is NbtArray<long> longArray)
        {
            Write(NbtTagType.LongArray);
            WriteStringInternal(array.Name);
            WriteIntInternal(longArray.Count);

            for (int i = 0; i < longArray.Count; i++)
                WriteLongInternal(longArray[i]);
        }
        else if (array is NbtArray<byte> byteArray)
        {
            Write(NbtTagType.ByteArray);
            WriteStringInternal(array.Name);
            WriteIntInternal(byteArray.Count);

            for (int i = 0; i < byteArray.Count; i++)
                BaseStream.Write(byteArray.GetArray());
        }
    }

    public void WriteString(string value)
    {
        Validate(null, NbtTagType.String);
        WriteStringInternal(value);
    }

    public void WriteString(string name, string value)
    {
        Validate(name, NbtTagType.String);

        Write(NbtTagType.String);
        WriteStringInternal(name);
        WriteStringInternal(value);
    }

    public void WriteByte(byte value)
    {
        Validate(null, NbtTagType.Byte);
        WriteByteInternal(value);
    }

    public void WriteByte(string name, byte value)
    {
        Validate(name, NbtTagType.Byte);

        Write(NbtTagType.Byte);
        WriteStringInternal(name);
        WriteByteInternal(value);
    }

    public void WriteBool(bool value)
    {
        Validate(null, NbtTagType.Byte);
        WriteByteInternal((byte)(value ? 1 : 0));
    }

    public void WriteBool(string name, bool value)
    {
        Validate(name, NbtTagType.Byte);

        Write(NbtTagType.Byte);
        WriteStringInternal(name);
        WriteByteInternal((byte)(value ? 1 : 0));
    }

    public void WriteShort(short value)
    {
        Validate(null, NbtTagType.Short);
        WriteShortInternal(value);
    }

    public void WriteShort(string name, short value)
    {
        Validate(name, NbtTagType.Short);

        Write(NbtTagType.Short);
        WriteStringInternal(name);
        WriteShortInternal(value);
    }

    public void WriteInt(int value)
    {
        Validate(null, NbtTagType.Int);
        WriteIntInternal(value);
    }

    public void WriteInt(string name, int value)
    {
        Validate(name, NbtTagType.Int);

        Write(NbtTagType.Int);
        WriteStringInternal(name);
        WriteIntInternal(value);
    }

    public void WriteLong(long value)
    {
        Validate(null, NbtTagType.Long);
        WriteLongInternal(value);
    }

    public void WriteLong(string name, long value)
    {
        Validate(name, NbtTagType.Long);

        Write(NbtTagType.Long);
        WriteStringInternal(name);
        WriteLongInternal(value);
    }

    public void WriteFloat(float value)
    {
        Validate(null, NbtTagType.Float);
        WriteFloatInternal(value);
    }

    public void WriteFloat(string name, float value)
    {
        Validate(name, NbtTagType.Float);

        Write(NbtTagType.Float);
        WriteStringInternal(name);
        WriteFloatInternal(value);
    }

    public void WriteDouble(double value)
    {
        Validate(null, NbtTagType.Double);
        WriteDoubleInternal(value);
    }

    public void WriteDouble(string name, double value)
    {
        Validate(name, NbtTagType.Double);

        Write(NbtTagType.Double);
        WriteStringInternal(name);

        WriteDoubleInternal(value);
    }

    public void Validate(string name, NbtTagType type)
    {
        if (RootType == NbtTagType.List)
        {
            if (!string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException($"Use the Write{type}({type.ToString().ToLower()} value) method when writing to lists");

            if (expectedListType != type)
                throw new InvalidOperationException($"Expected list type: {expectedListType}. Got: {type}");
            else if (!string.IsNullOrEmpty(name))
                throw new InvalidOperationException("Tags inside lists must be nameless.");
            else if (listIndex > listSize)
                throw new IndexOutOfRangeException("Exceeded pre-defined list size");

            listIndex++;
        }
        else if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException($"Tags inside a compound tag must have a name. Tag({type})");
    }

    public void TryFinish()
    {
        if (rootNodes.Count > 0)
            throw new InvalidOperationException("Unable to close writer. Some tags have yet to be closed.");//TODO maybe more info here??

        BaseStream.Flush();
    }

    public async Task TryFinishAsync()
    {
        if (rootNodes.Count > 0)
            throw new InvalidOperationException("Unable to close writer. Some tags have yet to be closed.");//TODO maybe more info here??

        await BaseStream.FlushAsync();
    }

    public ValueTask DisposeAsync() => BaseStream.DisposeAsync();
    public void Dispose() => BaseStream.Dispose();

    private class Node
    {
        public NbtTagType Type { get; set; }

        public int? ListSize { get; set; }

        public int? ListIndex { get; set; }

        public NbtTagType? ExpectedListType { get; set; }
    }
}
