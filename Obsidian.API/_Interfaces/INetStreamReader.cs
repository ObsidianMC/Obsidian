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

    /// <summary>
    /// Reads an id of the built-in registry <paramref name="registry"/> (e.g. <c>minecraft:block</c>), in this
    /// process's numbering where the connection numbers the registry differently. The default implementation, for
    /// streams that don't translate ids, reads it as it is.
    /// </summary>
    /// <exception cref="InvalidOperationException">This process lacks the connection's entry.</exception>
    public int ReadRegistryId(string registry) => this.ReadVarInt();

    /// <summary>
    /// Reads a holder of the registry <paramref name="registry"/> like vanilla's <c>ByteBufCodecs.holder</c>: its id
    /// (see <see cref="ReadRegistryId"/>), or null for a value written inline, which the caller reads next.
    /// </summary>
    public int? ReadRegistryHolder(string registry)
    {
        var id = this.ReadVarInt();
        return id == 0 ? null : id - 1;
    }

    /// <summary>
    /// Reads an ID set of the registry <paramref name="registry"/> (see <see cref="ReadRegistryId"/>).
    /// </summary>
    public IdSet ReadIdSet(string registry) => IdSet.Read(this, registry);

    /// <summary>Reads an ID set without translating its ids.</summary>
    [Obsolete("Name the set's registry with ReadIdSet(string), so its ids are translated across a connection.")]
    public IdSet ReadIdSet() => IdSet.Read(this);

    public SoundEvent ReadSoundEvent();

    public TValue[] ReadLengthPrefixedArray<TValue>(Func<TValue> read);

    public AttributeModifier ReadAttributeModifier();
    public Enchantment ReadEnchantment();

    public SignedMessage ReadSignedMessage();
    public ArgumentSignature ReadArgumentSignature();
    public DateTimeOffset ReadDateTimeOffset();

    public PotionEffectData ReadPotionEffectData();

    public Vector ReadPosition();
    public VectorF ReadPositionF();
    public VectorF ReadAbsoluteFloatPositionF();

    public SoundPosition ReadSoundPosition();

    public Angle ReadAngle();
    public Angle ReadFloatAngle();
    public ChatMessage ReadChat();
    public byte[] ReadByteArray();
    public byte[] ReadByteArray(int maxLength);
    public byte[] ReadRemainingBytes(int maxLength);
    public long[] ReadLongArray();
    public int[] ReadVarIntArray();
    public BitSet ReadBitSet();
    public BitSet ReadFixedBitSet(int size);
    public Obsidian.Nbt.NbtCompound ReadNbtCompound();
    public Obsidian.Nbt.NbtCompound? ReadOptionalNbtCompound();
    public Guid ReadGuid();

    public TValue? ReadOptional<TValue>() where TValue : INetworkSerializable<TValue>;
    public string? ReadOptionalString();
    public Guid? ReadOptionalGuid();
    public float? ReadOptionalFloat();
    public bool? ReadOptionalBoolean();
    public int? ReadOptionalInt();
    public DataComponent ReadDataComponent(DataComponentType type);
    public ItemStack? ReadItemStack();
    public ItemStack? ReadUntrustedItemStack();
    public ItemStack ReadRequiredItemStack();
    public ItemStack?[] ReadItemStackList();
    public IHashedItemStack? ReadHashedItemStack();
    public Velocity ReadVelocity();
}
