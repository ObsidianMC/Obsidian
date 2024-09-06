namespace Obsidian.API;
public interface INetStreamReader : INetStream
{
    public bool CanRead { get; }

    public sbyte ReadSignedByte();
    public byte ReadUnsignedByte();
    public bool ReadBoolean();
    public ushort ReadUnsignedShort();
    public short ReadShort();
    public int ReadInt();
    public long ReadLong();
    public ulong ReadUnsignedLong();
    public float ReadFloat();
    public double ReadDouble();
    public string ReadString(int maxLength = short.MaxValue);
    public int ReadVarInt();
    public byte[] ReadUInt8Array(int length = 0);
    public long ReadVarLong();

    public DateTimeOffset ReadDateTimeOffset();

    public Vector ReadPosition();
    public Vector ReadAbsolutePosition();
    public VectorF ReadPositionF();
    public VectorF ReadAbsolutePositionF();
    public VectorF ReadAbsoluteFloatPositionF();

    public SoundPosition ReadSoundPosition();

    public Angle ReadAngle();
    public Angle ReadFloatAngle();
    public ChatMessage ReadChat();
    public byte[] ReadByteArray();
    public Guid ReadGuid();
    public Guid? ReadOptionalGuid();
    public ItemStack ReadItemStack();
    public Velocity ReadVelocity();
}
