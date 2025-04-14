using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;

namespace Obsidian.API;
public interface INetStreamReader : INetStream
{
    public bool CanRead { get; }

    public byte ReadByte();
    public sbyte ReadSignedByte();

    public TEnum ReadSignedByte<TEnum>() where TEnum : Enum;
    public TEnum ReadUnsignedByte<TEnum>() where TEnum : Enum;
    public TEnum ReadVarInt<TEnum>() where TEnum : Enum;
    public TEnum ReadInt<TEnum>() where TEnum : Enum;
    
    public bool ReadBoolean();
    public ushort ReadUnsignedShort();
    public short ReadShort();
    public int ReadInt();

    public long ReadLong();
    public ulong ReadUnsignedLong();
    public float ReadSingle();
    public double ReadDouble();
    public string ReadString(int maxLength = short.MaxValue);
    public int ReadVarInt();
    
    public byte[] ReadUInt8Array(int length = 0);
    public long ReadVarLong();

    public IdSet ReadIdSet();
    public SoundEvent ReadSoundEvent();

    public List<TValue> ReadLengthPrefixedArray<TValue>(Func<TValue> read);

    public AttributeModifier ReadAttributeModifier();
    public Enchantment ReadEnchantment();

    public SignedMessage ReadSignedMessage();
    public ArgumentSignature ReadArgumentSignature();
    public DateTimeOffset ReadDateTimeOffset();

    public PotionEffectData ReadPotionEffectData();

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

    public TValue? ReadOptional<TValue>() where TValue : INetworkSerializable<TValue>;
    public string? ReadOptionalString();
    public Guid? ReadOptionalGuid();
    public float? ReadOptionalFloat();
    public bool? ReadOptionalBoolean();
    public int? ReadOptionalInt();
    public ItemStack? ReadItemStack();
    public Velocity ReadVelocity();
}
