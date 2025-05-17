using Obsidian.API.BlockStates;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Nbt;
using Obsidian.Serialization.Attributes;
using System.Buffers.Binary;
using System.IO;
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
    public Velocity ReadVelocity() => new(ReadShort(), ReadShort(), ReadShort());

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

        var componentsToAdd = this.ReadVarInt();
        var componentsToRemove = this.ReadVarInt();

        if (itemStack.Type == Material.Air)
            return itemStack;

        for (int i = 0; i < componentsToAdd; i++)
        {
            var type = this.ReadVarInt();

            itemStack.Add(ComponentBuilder.ComponentsMap[type]());
        }

        for (int i = 0; i < componentsToRemove; i++)
            itemStack.Remove(this.ReadVarInt<DataComponentType>());

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

    [ReadMethod]
    public ChatMessage ReadChat()
    {
        //TODO this can be sped up or done better
        using var ms = new MemoryStream(this.AsSpan((int)(this.size - this.offset)).ToArray());

        var reader = new NbtReader(ms);
        var chatMessage = ChatMessage.Empty;

        if(!reader.TryReadNextTag<NbtCompound>(false, out var root))
        {
            this.offset += (int)ms.Position;
            return chatMessage;
        }

        this.offset += (int)ms.Position;

        return chatMessage.FromNbt(root);
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
        List<int>? ids = type != 0 ? this.ReadLengthPrefixedArray(this.ReadVarInt) : null;

        return new() { Type = type, Ids = ids, TagName = tagName };
    }
    public SoundEvent ReadSoundEvent() => new()
    {
        ResourceLocation = this.ReadString(),
        FixedRange = this.ReadOptionalFloat()
    };

    public List<TValue> ReadLengthPrefixedArray<TValue>(Func<TValue> read)
    {
        var count = this.ReadVarInt();
        var list = new List<TValue>(count);

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
