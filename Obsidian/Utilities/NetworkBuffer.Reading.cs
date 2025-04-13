using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Nbt;
using Obsidian.Serialization.Attributes;
using System.Buffers.Binary;
using System.Text;

namespace Obsidian.Utilities;
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
        var data = this.data[(int)this.offset..length];

        this.offset += length;

        return new(data, 0, 1);
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
    public Guid? ReadOptionalGuid()
    {
        if (this.ReadBoolean())
            return this.ReadGuid();

        return null;
    }

    [ReadMethod]
    public Velocity ReadVelocity()
    {
        return new Velocity(ReadShort(), ReadShort(), ReadShort());
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
    public Angle ReadAngle() => new Angle(this.ReadByte());

    [ReadMethod]
    public ChatMessage ReadChat()
    {
        var reader = new NbtReader(this.data);
        var chatMessage = ChatMessage.Empty;

        return !reader.TryReadNextTag<NbtCompound>(false, out var root) ? chatMessage : chatMessage.FromNbt(root);
    }

    #region Generic Read Methods
    public byte ReadByte()
    {
        this.ValidateOffset();

        var readByte = this.data[(int)this.offset..ByteSize];

        this.offset += ByteSize;

        return readByte[0];
    }

    [ReadMethod]
    public string ReadString(int maxLength = 32767)
    {
        var length = ReadVarInt();
        var buffer = this.data[(int)this.offset..length];

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
        this.ValidateOffset();

        var buffer = this.data[(int)this.offset..LongSize];

        this.offset += LongSize;

        return BinaryPrimitives.ReadUInt64BigEndian(buffer);
    }

    public short ReadShort()
    {
        this.ValidateOffset();

        var buffer = this.data[(int)this.offset..ShortSize];

        this.offset += ShortSize;

        return BinaryPrimitives.ReadInt16BigEndian(buffer);
    }

    public int ReadInt()
    {
        this.ValidateOffset();

        var buffer = this.data[(int)this.offset..IntSize];

        this.offset += IntSize;

        return BinaryPrimitives.ReadInt32BigEndian(buffer);
    }

    public long ReadLong()
    {
        this.ValidateOffset();

        var buffer = this.data[(int)this.offset..LongSize];

        this.offset += LongSize;

        return BinaryPrimitives.ReadInt64BigEndian(buffer);
    }

    public double ReadDouble()
    {
        this.ValidateOffset();

        var buffer = this.data[(int)this.offset..LongSize];

        this.offset += LongSize;

        return BinaryPrimitives.ReadDoubleBigEndian(buffer);
    }

    public float ReadSingle()
    {
        this.ValidateOffset();

        var buffer = this.data[(int)this.offset..FloatSize];

        this.offset += FloatSize;

        return BinaryPrimitives.ReadSingleBigEndian(buffer);
    }

    #endregion

    private void ValidateOffset()
    {
        if (this.offset >= this.data.Length)
            throw new IndexOutOfRangeException("Reached end of buffer");
    }
}
