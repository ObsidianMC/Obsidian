using Obsidian.API.BlockStates;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Nbt;
using Obsidian.Serialization.Attributes;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Obsidian.Net;

public partial class NetworkBuffer : INetStreamReader
{
    private const int ByteSize = sizeof(byte);
    private const int ShortSize = sizeof(short);
    private const int IntSize = sizeof(int);
    private const int LongSize = sizeof(long);
    private const int FloatSize = sizeof(float);

    public bool CanRead { get; internal set; }

    public NetworkBuffer Read(int length)
    {
        var data = this.ReadUntil(length);

        return new(data);
    }

    [ReadMethod, VarLength]
    public int ReadVarInt()
    {
        int numRead = 0;
        int result = 0;
        byte read;
        do
        {
            read = this.ReadByte();
            int value = read & 0b01111111;
            result |= value << (7 * numRead);

            numRead++;
            if (numRead > 5)
            {
                throw new InvalidOperationException("VarInt is too big");
            }
        } while ((read & 0b10000000) != 0);

        return result;
    }

    [ReadMethod, VarLength]
    public long ReadVarLong()
    {
        int numRead = 0;
        long result = 0;
        byte read;
        do
        {
            read = this.ReadByte();
            int value = (read & 0b01111111);
            result |= (long)value << (7 * numRead);

            numRead++;
            if (numRead > 10)
            {
                throw new InvalidOperationException("VarLong is too big");
            }
        } while ((read & 0b10000000) != 0);

        return result;
    }

    [ReadMethod]
    public Guid ReadGuid() => GuidHelper.FromLongs(this.ReadLong(), this.ReadLong());

    [ReadMethod]
    public Guid? ReadOptionalGuid() => this.ReadBoolean() ? this.ReadGuid() : null;

    [ReadMethod]
    public Velocity ReadVelocity()
    {
        var firstByte = this.ReadByte();

        if (firstByte == 0)
            return Velocity.Zero;

        var secondByte = this.ReadByte();
        var remainingBytes = this.ReadInt();

        long packedData = remainingBytes << 16 | (secondByte << 8) | firstByte;

        long scaleFactor = firstByte & ScaleBits;

        if ((firstByte & ContinuiationBit) != 0)
            scaleFactor |= ((long)(uint)this.ReadVarInt()) << 2;

        var scaleFactorDouble = (double)scaleFactor;

        var xPacked = packedData >> 3;
        var yPacked = packedData >> 18;
        var zPacked = packedData >> 33;

        return new(Unpack(xPacked) * scaleFactorDouble, Unpack(yPacked) * scaleFactorDouble, Unpack(zPacked) * scaleFactorDouble);
    }

    public TValue? ReadOptional<TValue>() where TValue : INetworkSerializable<TValue> =>
        this.ReadBoolean() ? TValue.Read(this) : default;

    public Enchantment ReadEnchantment() => new()
    {
        Id = this.ReadVarInt(),
        Level = this.ReadVarInt(),
    };

    [ReadMethod]
    public ItemStack? ReadItemStack()
    {
        var count = this.ReadVarInt();

        if (count == 0)
            return null;

        var item = ItemsRegistry.Get(ReadVarInt());

        var itemStack = new ItemStack(item, count);

        if (itemStack.Type == Material.Air)
            return itemStack;

        var componentsToAdd = this.ReadVarInt();
        var componentsToRemove = this.ReadVarInt();

        for (int i = 0; i < componentsToAdd; i++)
        {
            var type = this.ReadVarInt();

            itemStack.Add(ComponentBuilder.ComponentsMap[type]());
        }

        for (int i = 0; i < componentsToRemove; i++)
            itemStack.Remove(this.ReadVarInt<DataComponentType>());

        return itemStack;
    }

    public IHashedItemStack? ReadHashedItemStack()
    {
        if (!this.ReadBoolean())
            return null;

        var item = ItemsRegistry.Get(ReadVarInt());
        var count = this.ReadVarInt();

        var itemStack = new HashedItemStack(item, count);

        //Might be best to change this
        if (itemStack.Type == Material.Air)
            return itemStack;

        var componentsToAdd = this.ReadVarInt();
        for (int i = 0; i < componentsToAdd; i++)
        {
            var type = this.ReadVarInt<DataComponentType>();

            itemStack.HashedComponents.Add(type, this.ReadInt());
        }

        var componentsToRemove = this.ReadVarInt();

        for (int i = 0; i < componentsToRemove; i++)
            itemStack.ComponentsToRemove.Add(this.ReadVarInt<DataComponentType>());

        return itemStack;
    }


    [ReadMethod]
    public DateTimeOffset ReadDateTimeOffset() => DateTimeOffset.FromUnixTimeMilliseconds(this.ReadLong());

    [ReadMethod]
    public Vector ReadPosition()
    {
        ulong value = this.ReadUnsignedLong();

        long x = (long)(value >> 38);
        long y = (long)(value & 0xFFF);
        long z = (long)(value << 26 >> 38);

        if (x >= Math.Pow(2, 25))
            x -= (long)Math.Pow(2, 26);

        if (y >= Math.Pow(2, 11))
            y -= (long)Math.Pow(2, 12);

        if (z >= Math.Pow(2, 25))
            z -= (long)Math.Pow(2, 26);

        return new Vector
        {
            X = (int)x,

            Y = (int)y,

            Z = (int)z,
        };
    }

    [ReadMethod, DataFormat(typeof(double))]
    public Vector ReadAbsolutePosition()
    {
        return new Vector
        {
            X = (int)ReadDouble(),
            Y = (int)ReadDouble(),
            Z = (int)ReadDouble()
        };
    }

    [ReadMethod]
    public VectorF ReadPositionF()
    {
        ulong value = this.ReadUnsignedLong();

        long x = (long)(value >> 38);
        long y = (long)(value & 0xFFF);
        long z = (long)(value << 26 >> 38);

        if (x >= Math.Pow(2, 25))
            x -= (long)Math.Pow(2, 26);

        if (y >= Math.Pow(2, 11))
            y -= (long)Math.Pow(2, 12);

        if (z >= Math.Pow(2, 25))
            z -= (long)Math.Pow(2, 26);

        return new VectorF
        {
            X = x,

            Y = y,

            Z = z,
        };
    }

    [ReadMethod, DataFormat(typeof(double))]
    public VectorF ReadAbsolutePositionF()
    {
        return new VectorF
        {
            X = (float)ReadDouble(),
            Y = (float)ReadDouble(),
            Z = (float)ReadDouble()
        };
    }

    [ReadMethod, DataFormat(typeof(float))]
    public VectorF ReadAbsoluteFloatPositionF()
    {
        return new VectorF
        {
            X = ReadSingle(),
            Y = ReadSingle(),
            Z = ReadSingle()
        };
    }

    [ReadMethod]
    public SoundPosition ReadSoundPosition() => new(this.ReadInt(), this.ReadInt(), this.ReadInt());

    [ReadMethod]
    public Angle ReadAngle() => new(this.ReadByte());

    /// <summary>
    /// Reads a text component as network NBT: a compound, or the plain string or list of components vanilla's
    /// component codec also writes.
    /// </summary>
    [ReadMethod]
    public ChatMessage ReadChat()
    {
        //TODO this can be sped up or done better
        using var ms = new MemoryStream(this.AsSpan((int)(this.size - this.offset)).ToArray());

        var found = new NbtReader(ms).TryReadNextTag(false, out INbtTag? tag);
        this.offset += (int)ms.Position;
        this.BytesPending -= (int)ms.Position;

        return found ? tag!.TextFromNbt() ?? ChatMessage.Empty : ChatMessage.Empty;
    }

    #region Generic Read Methods
    public byte ReadByte()
    {
        var buffer = this.ReadUntil(ByteSize);

        return buffer[0];
    }

    public sbyte ReadSignedByte() => (sbyte)this.ReadByte();

    [ReadMethod]
    public string ReadString(int maxLength = 32767)
    {
        var length = ReadVarInt();
        var buffer = this.ReadUntil(length);

        var value = Encoding.UTF8.GetString(buffer);
        if (maxLength > 0 && value.Length > maxLength)
            throw new ArgumentException($"string ({value.Length}) exceeded maximum length ({maxLength})", nameof(maxLength));

        return value;
    }

    [ReadMethod]
    public bool ReadBoolean() => this.ReadByte() == 1;

    public TEnum ReadSignedByte<TEnum>() where TEnum : Enum => (TEnum)Enum.Parse(typeof(TEnum), this.ReadByte().ToString());
    public TEnum ReadUnsignedByte<TEnum>() where TEnum : Enum => (TEnum)Enum.Parse(typeof(TEnum), this.ReadByte().ToString());
    public TEnum ReadInt<TEnum>() where TEnum : Enum => (TEnum)Enum.Parse(typeof(TEnum), this.ReadInt().ToString());
    public TEnum ReadVarInt<TEnum>() where TEnum : Enum => (TEnum)Enum.Parse(typeof(TEnum), this.ReadVarInt().ToString());

    [ReadMethod]
    public ulong ReadUnsignedLong()
    {
        var buffer = this.ReadUntil(LongSize);

        return BinaryPrimitives.ReadUInt64BigEndian(buffer);
    }

    public short ReadShort()
    {
        var buffer = this.ReadUntil(ShortSize);

        return BinaryPrimitives.ReadInt16BigEndian(buffer);
    }

    public int ReadInt()
    {
        var buffer = this.ReadUntil(IntSize);

        return BinaryPrimitives.ReadInt32BigEndian(buffer);
    }

    public long ReadLong()
    {
        var buffer = this.ReadUntil(LongSize);

        return BinaryPrimitives.ReadInt64BigEndian(buffer);
    }

    public double ReadDouble()
    {
        var buffer = this.ReadUntil(LongSize);

        return BinaryPrimitives.ReadDoubleBigEndian(buffer);
    }

    public float ReadSingle()
    {
        var buffer = this.ReadUntil(FloatSize);

        return BinaryPrimitives.ReadSingleBigEndian(buffer);
    }

    public ushort ReadUnsignedShort()
    {
        var buffer = this.ReadUntil(ShortSize);

        return BinaryPrimitives.ReadUInt16BigEndian(buffer);
    }

    protected virtual byte[] ReadUntil(int size)
    {
        this.ValidateOffset();

        var span = this.AsSpan(size);

        this.offset += size;
        this.BytesPending -= size;

        return span.ToArray();
    }

    #endregion


    [ReadMethod]
    public byte[] ReadByteArray()
    {
        var length = ReadVarInt();
        return ReadUInt8Array(length);
    }

    /// <summary>Reads a VarInt length, then that many bytes; throws when the length is over <paramref name="maxLength"/>.</summary>
    public byte[] ReadByteArray(int maxLength)
    {
        var length = this.ReadVarInt();
        if (length < 0 || length > maxLength)
            throw new InvalidDataException($"Byte array length {length} is outside 0 to {maxLength}.");

        // ReadUntil throws at the end of the buffer even for no bytes, and an empty array can end a packet.
        return length == 0 ? [] : this.ReadUntil(length);
    }

    /// <summary>Reads the rest of the buffer; throws when it's longer than <paramref name="maxLength"/>.</summary>
    public byte[] ReadRemainingBytes(int maxLength)
    {
        var length = (int)(this.size - this.offset);
        if (length > maxLength)
            throw new InvalidDataException($"{length} remaining bytes are more than {maxLength}.");

        return length == 0 ? [] : this.ReadUntil(length);
    }

    /// <summary>Reads a VarInt count, then that many longs.</summary>
    public long[] ReadLongArray()
    {
        var values = new long[this.ReadCount(LongSize)];
        for (var i = 0; i < values.Length; i++)
            values[i] = this.ReadLong();
        return values;
    }

    /// <summary>Reads a VarInt count, then that many VarInts.</summary>
    public int[] ReadVarIntArray()
    {
        var values = new int[this.ReadCount(ByteSize)];
        for (var i = 0; i < values.Length; i++)
            values[i] = this.ReadVarInt();
        return values;
    }

    /// <summary>
    /// Reads a VarInt count of values at least <paramref name="minimumSize"/> bytes each, rejecting counts the rest of
    /// the buffer can't hold before anything is allocated for them.
    /// </summary>
    private int ReadCount(int minimumSize)
    {
        var count = this.ReadVarInt();
        if (count < 0 || count > (this.size - this.offset) / minimumSize)
            throw new InvalidDataException($"Count {count} is more than the rest of the packet holds.");
        return count;
    }

    /// <summary>Reads a bit set as a VarInt count of longs, then the longs (Java's <c>BitSet.toLongArray</c>).</summary>
    public BitSet ReadBitSet() => new(this.ReadLongArray());

    /// <summary>
    /// Reads a bit set of <paramref name="size"/> bits as <c>ceil(size / 8)</c> bytes, lowest bit first (Java's
    /// <c>BitSet.toByteArray</c>, padded to the size).
    /// </summary>
    public BitSet ReadFixedBitSet(int size)
    {
        var bytes = this.ReadUntil((size + 7) / 8);
        var bits = new BitSet();
        for (var i = 0; i < size; i++)
            bits.SetBit(i, (bytes[i / 8] & (1 << (i % 8))) != 0);
        return bits;
    }

    /// <summary>Reads a network NBT compound (no root name); throws when it's an empty (end) tag.</summary>
    public NbtCompound ReadNbtCompound() =>
        this.ReadOptionalNbtCompound() ?? throw new InvalidDataException("Expected an NBT compound, but found an end tag.");

    /// <summary>Reads a network NBT compound (no root name), or null for an empty (end) tag.</summary>
    public NbtCompound? ReadOptionalNbtCompound()
    {
        using var stream = new MemoryStream(this.AsSpan((int)(this.size - this.offset)).ToArray());
        var found = new NbtReader(stream).TryReadNextTag<NbtCompound>(false, out var compound);

        this.offset += (int)stream.Position;
        this.BytesPending -= (int)stream.Position;
        return found ? compound : null;
    }

    [ReadMethod]
    public byte[] ReadUInt8Array(int length = 0)
    {
        if (length == 0)
            length = ReadVarInt();

        var result = this.ReadUntil(length);

        return result;
    }

    public IdSet ReadIdSet()
    {
        var type = this.ReadVarInt();
        string? tagName = type == 0 ? tagName = this.ReadString() : null;
        ImmutableArray<int>? ids = type != 0 ? ImmutableCollectionsMarshal.AsImmutableArray(this.ReadLengthPrefixedArray(this.ReadVarInt)) : null;

        return new() { Type = type, Ids = ids, TagName = tagName };
    }
    public SoundEvent ReadSoundEvent() => new()
    {
        ResourceLocation = this.ReadString(),
        FixedRange = this.ReadOptionalFloat()
    };

    public TValue[] ReadLengthPrefixedArray<TValue>(Func<TValue> read)
    {
        var count = this.ReadVarInt();
        var list = new TValue[count];

        for (var i = 0; i < count; i++)
            list[i] = read();

        return list;
    }

    public AttributeModifier ReadAttributeModifier() => new()
    {
        Id = this.ReadVarInt(),
        Uuid = this.ReadGuid(),
        Name = this.ReadString(),
        Value = this.ReadDouble(),
        Operation = this.ReadVarInt<AttributeOperation>(),
        Slot = this.ReadVarInt<AttributeSlot>()
    };

    [ReadMethod]
    public SignedMessage ReadSignedMessage() =>
        new() { UserId = this.ReadGuid(), Signature = this.ReadUInt8Array(256) };

    [ReadMethod]
    public ArgumentSignature ReadArgumentSignature() => new()
    {
        ArgumentName = this.ReadString(16),
        Signature = this.ReadUInt8Array(256)
    };

    public PotionEffectData ReadPotionEffectData() => new()
    {
        Id = this.ReadVarInt(),
        Amplifier = this.ReadVarInt(),
        Duration = this.ReadVarInt(),
        Ambient = this.ReadBoolean(),
        ShowIcon = this.ReadBoolean(),
        ShowParticles = this.ReadBoolean(),
        HiddenEffect = this.ReadBoolean() ? this.ReadPotionEffectData() : null
    };

    [ReadMethod, DataFormat(typeof(float))]
    public Angle ReadFloatAngle() => ReadSingle();

    public int? ReadOptionalInt() => this.ReadBoolean() ? this.ReadInt() : null;
    public float? ReadOptionalFloat() => this.ReadBoolean() ? this.ReadSingle() : null;
    public bool? ReadOptionalBoolean() => this.ReadBoolean() ? this.ReadBoolean() : null;
    public string? ReadOptionalString() => this.ReadBoolean() ? this.ReadString() : null;

    protected void ValidateOffset()
    {
        if (this.offset >= this.data.Length)
            throw new IndexOutOfRangeException("Reached end of buffer");
    }
}
