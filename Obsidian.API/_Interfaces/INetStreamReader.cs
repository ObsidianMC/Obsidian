using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;

namespace Obsidian.API;
/// <summary>
/// Reads packet data in Minecraft's protocol formats, advancing <see cref="INetStream.Offset"/>. Reads past the end
/// of the data throw.
/// </summary>
public interface INetStreamReader : INetStream
{
    /// <summary>
    /// Gets a value indicating whether the stream can be read.
    /// </summary>
    public bool CanRead { get; }

    /// <summary>
    /// Reads an unsigned byte.
    /// </summary>
    public byte ReadByte();
    /// <summary>
    /// Reads a signed byte.
    /// </summary>
    public sbyte ReadSignedByte();

    /// <summary>
    /// Reads a byte as the member of <typeparamref name="TEnum"/> with that value.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    public TEnum ReadSignedByte<TEnum>() where TEnum : Enum;
    /// <summary>
    /// Reads an unsigned byte as the member of <typeparamref name="TEnum"/> with that value.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    public TEnum ReadUnsignedByte<TEnum>() where TEnum : Enum;
    /// <summary>
    /// Reads a VarInt as the member of <typeparamref name="TEnum"/> with that value.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    public TEnum ReadVarInt<TEnum>() where TEnum : Enum;
    /// <summary>
    /// Reads a 32-bit big-endian integer as the member of <typeparamref name="TEnum"/> with that value.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    public TEnum ReadInt<TEnum>() where TEnum : Enum;
    
    /// <summary>
    /// Reads a byte as a boolean: 1 is <see langword="true"/>, anything else <see langword="false"/>.
    /// </summary>
    public bool ReadBoolean();
    /// <summary>
    /// Reads a 16-bit big-endian unsigned integer.
    /// </summary>
    public ushort ReadUnsignedShort();
    /// <summary>
    /// Reads a 16-bit big-endian integer.
    /// </summary>
    public short ReadShort();
    /// <summary>
    /// Reads a 32-bit big-endian integer.
    /// </summary>
    public int ReadInt();

    /// <summary>
    /// Reads a 64-bit big-endian integer.
    /// </summary>
    public long ReadLong();
    /// <summary>
    /// Reads a 64-bit big-endian unsigned integer.
    /// </summary>
    public ulong ReadUnsignedLong();
    /// <summary>
    /// Reads a big-endian single-precision float.
    /// </summary>
    public float ReadSingle();
    /// <summary>
    /// Reads a big-endian double-precision float.
    /// </summary>
    public double ReadDouble();
    /// <summary>
    /// Reads a UTF-8 string prefixed with its length in bytes as a VarInt.
    /// </summary>
    /// <param name="maxLength">The most characters allowed, or 0 or less for no limit.</param>
    /// <exception cref="ArgumentException">The string is longer than <paramref name="maxLength"/>.</exception>
    public string ReadString(int maxLength = short.MaxValue);
    /// <summary>
    /// Reads a variable-length 32-bit integer (VarInt).
    /// </summary>
    public int ReadVarInt();
    
    /// <summary>
    /// Reads <paramref name="length"/> bytes.
    /// </summary>
    /// <param name="length">The number of bytes, or 0 to read a VarInt length first.</param>
    public byte[] ReadUInt8Array(int length = 0);
    /// <summary>
    /// Reads a variable-length 64-bit integer (VarLong).
    /// </summary>
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

    /// <summary>
    /// Reads an inline sound event: its identifier and optional fixed range.
    /// </summary>
    public SoundEvent ReadSoundEvent();

    /// <summary>
    /// Reads a VarInt count, then that many values with <paramref name="read"/>.
    /// </summary>
    /// <typeparam name="TValue">The element type.</typeparam>
    /// <param name="read">Reads one element.</param>
    public TValue[] ReadLengthPrefixedArray<TValue>(Func<TValue> read);

    /// <summary>
    /// Reads an attribute modifier.
    /// </summary>
    public AttributeModifier ReadAttributeModifier();
    /// <summary>
    /// Reads an enchantment's id and level, as VarInts.
    /// </summary>
    public Enchantment ReadEnchantment();

    /// <summary>
    /// Reads a chat message signature: the sender's UUID and a 256-byte signature.
    /// </summary>
    public SignedMessage ReadSignedMessage();
    /// <summary>
    /// Reads a signed command argument's name and its 256-byte signature.
    /// </summary>
    public ArgumentSignature ReadArgumentSignature();
    /// <summary>
    /// Reads a time as milliseconds since the Unix epoch, a 64-bit integer.
    /// </summary>
    public DateTimeOffset ReadDateTimeOffset();

    /// <summary>
    /// Reads a potion effect instance.
    /// </summary>
    public PotionEffectData ReadPotionEffectData();

    /// <summary>
    /// Reads a block position packed into a 64-bit integer (26 bits of x, 26 of z, 12 of y).
    /// </summary>
    public Vector ReadPosition();
    /// <summary>
    /// Reads a packed block position, like <see cref="ReadPosition"/>, as a <see cref="VectorF"/>.
    /// </summary>
    public VectorF ReadPositionF();
    /// <summary>
    /// Reads a position as three floats: x, y and z.
    /// </summary>
    public VectorF ReadAbsoluteFloatPositionF();

    /// <summary>
    /// Reads a sound's position as three 32-bit integers.
    /// </summary>
    public SoundPosition ReadSoundPosition();

    /// <summary>
    /// Reads an angle stored in one byte, in 256ths of a turn.
    /// </summary>
    public Angle ReadAngle();
    /// <summary>
    /// Reads an angle stored as a float, in degrees.
    /// </summary>
    public Angle ReadFloatAngle();
    /// <summary>
    /// Reads a text component as network NBT.
    /// </summary>
    public ChatMessage ReadChat();
    /// <summary>
    /// Reads a VarInt length, then that many bytes.
    /// </summary>
    public byte[] ReadByteArray();
    /// <summary>
    /// Reads a VarInt length, then that many bytes.
    /// </summary>
    /// <param name="maxLength">The most bytes allowed.</param>
    /// <exception cref="System.IO.InvalidDataException">The length is negative or more than <paramref name="maxLength"/>.</exception>
    public byte[] ReadByteArray(int maxLength);
    /// <summary>
    /// Reads every byte left in the stream.
    /// </summary>
    /// <param name="maxLength">The most bytes allowed.</param>
    /// <exception cref="System.IO.InvalidDataException">More than <paramref name="maxLength"/> bytes are left.</exception>
    public byte[] ReadRemainingBytes(int maxLength);
    /// <summary>
    /// Reads a VarInt count, then that many 64-bit integers.
    /// </summary>
    public long[] ReadLongArray();
    /// <summary>
    /// Reads a VarInt count, then that many VarInts.
    /// </summary>
    public int[] ReadVarIntArray();
    /// <summary>
    /// Reads a bit set as a length-prefixed array of 64-bit integers.
    /// </summary>
    public BitSet ReadBitSet();
    /// <summary>
    /// Reads a bit set of <paramref name="size"/> bits stored in <c>ceil(size / 8)</c> bytes, lowest bit first.
    /// </summary>
    /// <param name="size">The number of bits.</param>
    public BitSet ReadFixedBitSet(int size);
    /// <summary>
    /// Reads a network NBT compound (one with no root name).
    /// </summary>
    /// <exception cref="System.IO.InvalidDataException">The tag is empty, isn't a compound, or is invalid.</exception>
    public Obsidian.Nbt.NbtCompound ReadNbtCompound();
    /// <summary>
    /// Reads a network NBT compound, or <see langword="null"/> for an empty (end) tag.
    /// </summary>
    /// <exception cref="System.IO.InvalidDataException">The tag isn't a compound, or is invalid.</exception>
    public Obsidian.Nbt.NbtCompound? ReadOptionalNbtCompound();
    /// <summary>
    /// Reads a UUID as two 64-bit integers, most significant first.
    /// </summary>
    public Guid ReadGuid();

    /// <summary>
    /// Reads a boolean, then a <typeparamref name="TValue"/> if it was <see langword="true"/>.
    /// </summary>
    /// <typeparam name="TValue">The value's type.</typeparam>
    public TValue? ReadOptional<TValue>() where TValue : INetworkSerializable<TValue>;
    /// <summary>
    /// Reads a boolean, then a string if it was <see langword="true"/>.
    /// </summary>
    public string? ReadOptionalString();
    /// <summary>
    /// Reads a boolean, then a UUID if it was <see langword="true"/>.
    /// </summary>
    public Guid? ReadOptionalGuid();
    /// <summary>
    /// Reads a boolean, then a float if it was <see langword="true"/>.
    /// </summary>
    public float? ReadOptionalFloat();
    /// <summary>
    /// Reads a boolean, then another boolean if it was <see langword="true"/>.
    /// </summary>
    public bool? ReadOptionalBoolean();
    /// <summary>
    /// Reads a boolean, then a 32-bit integer if it was <see langword="true"/>.
    /// </summary>
    public int? ReadOptionalInt();
    /// <summary>
    /// Reads the value of a data component of type <paramref name="type"/>.
    /// </summary>
    /// <param name="type">The component's type, read before it.</param>
    public DataComponent ReadDataComponent(DataComponentType type);
    /// <summary>
    /// Reads an item stack: its count, item and component changes, or <see langword="null"/> for an empty slot.
    /// </summary>
    public ItemStack? ReadItemStack();
    /// <summary>
    /// Reads an item stack as a creative-mode client sends it, with each component prefixed by its length, or
    /// <see langword="null"/> for an empty slot.
    /// </summary>
    public ItemStack? ReadUntrustedItemStack();
    /// <summary>
    /// Reads an item stack that can't be empty.
    /// </summary>
    /// <exception cref="System.IO.InvalidDataException">The slot is empty.</exception>
    public ItemStack ReadRequiredItemStack();

    /// <summary>Reads a nonempty item template (item ID, count, component patch), used by nested components.</summary>
    public ItemStack ReadItemStackTemplate();
    /// <summary>
    /// Reads a VarInt count, then that many item stacks.
    /// </summary>
    public ItemStack?[] ReadItemStackList();
    /// <summary>
    /// Reads an item stack as a client describes it in a container click, with component hashes instead of components,
    /// or <see langword="null"/> for an empty slot.
    /// </summary>
    public IHashedItemStack? ReadHashedItemStack();
    /// <summary>
    /// Reads a velocity in vanilla's packed (LpVec3) format.
    /// </summary>
    public Velocity ReadVelocity();
}
