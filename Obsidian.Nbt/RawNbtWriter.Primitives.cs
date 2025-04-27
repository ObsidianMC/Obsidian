using System.Buffers.Binary;

namespace Obsidian.Nbt;
public partial class RawNbtWriter
{
    public void WriteString(string value)
    {
        this.Validate(null, NbtTagType.String);
        this.Write(value);
    }

    public void WriteString(string name, string value)
    {
        this.Validate(name, NbtTagType.String);

        this.Write(NbtTagType.String);
        this.Write(name);
        this.Write(value);
    }

    public void WriteByte(byte value)
    {
        this.Validate(null, NbtTagType.Byte);
        this.Write(value);
    }

    public void WriteByte(string name, byte value)
    {
        this.Validate(name, NbtTagType.Byte);

        this.Write(NbtTagType.Byte);
        this.Write(name);
        this.Write(value);
    }

    public unsafe void WriteBool(bool value) => this.WriteByte(*(byte*)&value);

    public unsafe void WriteBool(string name, bool value) => this.WriteByte(name, *(byte*)&value);

    public void WriteShort(short value)
    {
        this.Validate(null, NbtTagType.Short);
        this.Write(value);
    }

    public void WriteShort(string name, short value)
    {
        this.Validate(name, NbtTagType.Short);

        this.Write(NbtTagType.Short);
        this.Write(name);
        this.Write(value);
    }

    public void WriteInt(int value)
    {
        this.Validate(null, NbtTagType.Int);
        this.Write(value);
    }

    public void WriteInt(string name, int value)
    {
        this.Validate(name, NbtTagType.Int);

        this.Write(NbtTagType.Int);
        this.Write(name);
        this.Write(value);
    }

    public void WriteLong(long value)
    {
        this.Validate(null, NbtTagType.Long);
        this.Write(value);
    }

    public void WriteLong(string name, long value)
    {
        this.Validate(name, NbtTagType.Long);

        this.Write(NbtTagType.Long);
        this.Write(name);
        this.Write(value);
    }

    public void WriteFloat(float value)
    {
        this.Validate(null, NbtTagType.Float);
        this.Write(value);
    }

    public void WriteFloat(string name, float value)
    {
        this.Validate(name, NbtTagType.Float);

        this.Write(NbtTagType.Float);
        this.Write(name);
        this.Write(value);
    }

    public void WriteDouble(double value)
    {
        this.Validate(null, NbtTagType.Double);
        this.Write(value);
    }

    public void WriteDouble(string name, double value)
    {
        this.Validate(name, NbtTagType.Double);

        this.Write(NbtTagType.Double);
        this.Write(name);
        this.Write(value);
    }


    #region primitive writing
    private void Write(string value)
    {
        if (value.Length > short.MaxValue)
            throw new InvalidOperationException($"value length must be less than {short.MaxValue}");

        if (!ModifiedUtf8.TryGetBytes(value, out var buffer))
            throw new InvalidOperationException("Failed to get bytes from string.");

        this.Write((short)buffer.Length);
        this.Write(buffer);
    }

    private void Write(byte value) => this.Write([value]);

    private void Write(short value)
    {
        Span<byte> span = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(span, value);
        this.Write(span);
    }

    private void Write(int value)
    {
        Span<byte> span = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(span, value);
        this.Write(span);
    }

    private void Write(long value)
    {
        Span<byte> span = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(span, value);
        this.Write(span);
    }

    private void Write(double value)
    {
        Span<byte> span = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(span, value);
        this.Write(span);
    }

    private void Write(float value)
    {
        Span<byte> span = stackalloc byte[4];
        BinaryPrimitives.WriteSingleBigEndian(span, value);
        this.Write(span);
    }

    private void Write(ReadOnlySpan<byte> buffer)
    {
        buffer.CopyTo(new Span<byte>(this.data, this.offset, buffer.Length));
        this.offset += buffer.Length;
    }
    #endregion

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
}
