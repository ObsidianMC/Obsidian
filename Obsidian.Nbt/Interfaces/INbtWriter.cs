namespace Obsidian.Nbt.Interfaces;
public interface INbtWriter : IDisposable, IAsyncDisposable
{
    public NbtTagType? RootType { get; }

    public bool Networked { get; }

    public void WriteCompoundStart(string name = "");
    public void WriteListStart(string name, NbtTagType listType, int length, bool writeName = true);
    public void EndList();
    public void EndCompound();

    public void WriteByte(byte value);
    public void WriteByte(string name, byte value);

    public void WriteShort(short value);
    public void WriteShort(string name, short value);

    public void WriteInt(int value);
    public void WriteInt(string name, int value);

    public void WriteLong(long value);
    public void WriteLong(string name, long value);

    public void WriteDouble(double value);
    public void WriteDouble(string name, double value);

    public void WriteFloat(float value);
    public void WriteFloat(string name, float value);

    public void WriteBool(bool value);
    public void WriteBool(string name, bool value);

    public void WriteString(string value);
    public void WriteString(string name, string value);

    public void Write(NbtTagType tagType);
    public void WriteTag(INbtTag tag);

    public void WriteListTag(INbtTag tag);

    public void WriteArray(string? name, ReadOnlySpan<int> values);
    public void WriteArray(string? name, ReadOnlySpan<long> values);
    public void WriteArray(string? name, ReadOnlySpan<byte> values);

    public void TryFinish();

    public Task TryFinishAsync();
}
