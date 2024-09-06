namespace Obsidian.API;
public interface INetStreamWriter : INetStream
{
    public bool CanWrite { get; }
    public void WriteByte(sbyte value);
    public void WriteUnsignedByte(byte value);
    public void WriteBoolean(bool value);

    public void WriteUnsignedShort(ushort value);
    public void WriteShort(short value);

    public void WriteInt(int value);

    public void WriteLong(long value);

    public void WriteFloat(float value);
    public void WriteDouble(double value);

    public void WriteString(string value, int maxLength = short.MaxValue);
    public void WriteVarInt(int value);
    public void WriteVarInt(Enum value);

    public void WriteLongArray(long[] values);
    public void WriteVarLong(long value);

    public void WriteBitSet(BitSet bitset, bool isFixed = false);
    public void WriteChat(ChatMessage chatMessage);
    public void WriteItemStack(ItemStack itemStack);
    public void WriteDateTimeOffset(DateTimeOffset date);
    public void WriteSoundEffect(SoundEffect sound);
    public void WriteByteArray(byte[] values);
    public void WriteUuid(Guid value);

    public byte[] ToArray();
}
