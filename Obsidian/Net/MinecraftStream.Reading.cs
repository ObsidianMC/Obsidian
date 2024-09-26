﻿using Obsidian.API;
using Obsidian.API.ItemComponents;
using Obsidian.API.Utilities;
using Obsidian.Commands;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Serialization.Attributes;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Obsidian.Net;

public partial class MinecraftStream : INetStreamReader
{

    [ReadMethod]
    public sbyte ReadSignedByte() => (sbyte)this.ReadUnsignedByte();

    public async Task<sbyte> ReadByteAsync() => (sbyte)await this.ReadUnsignedByteAsync();

    [ReadMethod]
    public byte ReadUnsignedByte()
    {
        Span<byte> buffer = stackalloc byte[1];
        BaseStream.ReadExactly(buffer);
        return buffer[0];
    }

    public async Task<byte> ReadUnsignedByteAsync()
    {
        var buffer = new byte[1];
        await this.ReadAsync(buffer);
        return buffer[0];
    }

    [ReadMethod]
    public bool ReadBoolean()
    {
        return ReadUnsignedByte() == 0x01;
    }

    public async Task<bool> ReadBooleanAsync()
    {
        var value = (int)await this.ReadByteAsync();
        return value switch
        {
            0x00 => false,
            0x01 => true,
            _ => throw new ArgumentOutOfRangeException("Byte returned by stream is out of range (0x00 or 0x01)",
                nameof(BaseStream))
        };
    }

    [ReadMethod]
    public ushort ReadUnsignedShort()
    {
        Span<byte> buffer = stackalloc byte[2];
        this.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt16BigEndian(buffer);
    }

    public async Task<ushort> ReadUnsignedShortAsync()
    {
        var buffer = new byte[2];
        await this.ReadAsync(buffer);
        return BinaryPrimitives.ReadUInt16BigEndian(buffer);
    }

    [ReadMethod]
    public short ReadShort()
    {
        Span<byte> buffer = stackalloc byte[2];
        this.ReadExactly(buffer);
        return BinaryPrimitives.ReadInt16BigEndian(buffer);
    }

    public async Task<short> ReadShortAsync()
    {
        using var buffer = new RentedArray<byte>(sizeof(short));
        await this.ReadExactlyAsync(buffer);
        return BinaryPrimitives.ReadInt16BigEndian(buffer);
    }

    [ReadMethod]
    public int ReadInt()
    {
        Span<byte> buffer = stackalloc byte[4];
        this.ReadExactly(buffer);
        return BinaryPrimitives.ReadInt32BigEndian(buffer);
    }

    public async Task<int> ReadIntAsync()
    {
        using var buffer = new RentedArray<byte>(sizeof(int));
        await this.ReadExactlyAsync(buffer);
        return BinaryPrimitives.ReadInt32BigEndian(buffer);
    }

    [ReadMethod]
    public long ReadLong()
    {
        Span<byte> buffer = stackalloc byte[8];
        this.ReadExactly(buffer);
        return BinaryPrimitives.ReadInt64BigEndian(buffer);
    }

    public async Task<long> ReadLongAsync()
    {
        using var buffer = new RentedArray<byte>(sizeof(long));
        await this.ReadExactlyAsync(buffer);
        return BinaryPrimitives.ReadInt64BigEndian(buffer);
    }

    [ReadMethod]
    public ulong ReadUnsignedLong()
    {
        Span<byte> buffer = stackalloc byte[8];
        this.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt64BigEndian(buffer);
    }

    public async Task<ulong> ReadUnsignedLongAsync()
    {
        using var buffer = new RentedArray<byte>(sizeof(ulong));
        await this.ReadExactlyAsync(buffer);
        return BinaryPrimitives.ReadUInt64BigEndian(buffer);
    }

    [ReadMethod]
    public float ReadFloat()
    {
        Span<byte> buffer = stackalloc byte[4];
        this.ReadExactly(buffer);
        return BinaryPrimitives.ReadSingleBigEndian(buffer);
    }

    public async Task<float> ReadFloatAsync()
    {
        using var buffer = new RentedArray<byte>(sizeof(float));
        await this.ReadExactlyAsync(buffer);
        return BinaryPrimitives.ReadSingleBigEndian(buffer);
    }

    [ReadMethod]
    public double ReadDouble()
    {
        Span<byte> buffer = stackalloc byte[8];
        this.ReadExactly(buffer);
        return BinaryPrimitives.ReadDoubleBigEndian(buffer);
    }

    public async Task<double> ReadDoubleAsync()
    {
        using var buffer = new RentedArray<byte>(sizeof(double));
        await this.ReadExactlyAsync(buffer);
        return BinaryPrimitives.ReadDoubleBigEndian(buffer);
    }

    [ReadMethod]
    public string ReadString(int maxLength = 32767)
    {
        var length = ReadVarInt();
        var buffer = new byte[length];
        this.ReadExactly(buffer);

        var value = Encoding.UTF8.GetString(buffer);
        if (maxLength > 0 && value.Length > maxLength)
        {
            throw new ArgumentException($"string ({value.Length}) exceeded maximum length ({maxLength})", nameof(value));
        }
        return value;
    }

    public async Task<string> ReadStringAsync(int maxLength = 32767)
    {
        var length = await this.ReadVarIntAsync();
        using var buffer = new RentedArray<byte>(length);
        if (BitConverter.IsLittleEndian)
        {
            buffer.Span.Reverse();
        }
        await this.ReadExactlyAsync(buffer);

        var value = Encoding.UTF8.GetString(buffer);
        if (maxLength > 0 && value.Length > maxLength)
        {
            throw new ArgumentException($"string ({value.Length}) exceeded maximum length ({maxLength})", nameof(maxLength));
        }
        return value;
    }

    [ReadMethod, VarLength]
    public int ReadVarInt()
    {
        int numRead = 0;
        int result = 0;
        byte read;
        do
        {
            read = this.ReadUnsignedByte();
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

    public virtual async Task<int> ReadVarIntAsync()
    {
        int numRead = 0;
        int result = 0;
        byte read;
        do
        {
            read = await this.ReadUnsignedByteAsync();
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

    [ReadMethod]
    public byte[] ReadUInt8Array(int length = 0)
    {
        if (length == 0)
            length = ReadVarInt();

        var result = new byte[length];
        if (length == 0)
            return result;

        int n = length;
        while (true)
        {
            n -= Read(result, length - n, n);
            if (n == 0)
                break;
        }
        return result;
    }

    public async Task<byte[]> ReadUInt8ArrayAsync(int length = 0)
    {
        if (length == 0)
            length = await this.ReadVarIntAsync();

        var result = new byte[length];
        if (length == 0)
            return result;

        int n = length;
        while (true)
        {
            n -= await this.ReadAsync(result, length - n, n);
            if (n == 0)
                break;
        }
        return result;
    }

    public async Task<byte> ReadUInt8Async()
    {
        int value = await this.ReadByteAsync();
        if (value == -1)
            throw new EndOfStreamException();
        return (byte)value;
    }

    [ReadMethod, VarLength]
    public long ReadVarLong()
    {
        int numRead = 0;
        long result = 0;
        byte read;
        do
        {
            read = this.ReadUnsignedByte();
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

    public async Task<long> ReadVarLongAsync()
    {
        int numRead = 0;
        long result = 0;
        byte read;
        do
        {
            read = await this.ReadUnsignedByteAsync();
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
    public SignedMessage ReadSignedMessage() =>
        new()
        {
            UserId = this.ReadGuid(),
            Signature = this.ReadUInt8Array()
        };

    [ReadMethod]
    public SignatureData ReadSignatureData() => new()
    {
        ExpirationTime = this.ReadDateTimeOffset(),
        PublicKey = this.ReadByteArray(),
        Signature = this.ReadByteArray()
    };

    [ReadMethod]
    public DateTimeOffset ReadDateTimeOffset() => DateTimeOffset.FromUnixTimeMilliseconds(this.ReadLong());

    [ReadMethod]
    public ArgumentSignature ReadArgumentSignature() => new()
    {
        ArgumentName = this.ReadString(),
        Signature = this.ReadByteArray()
    };

    [ReadMethod]
    public List<ArgumentSignature> ReadArgumentSignatures()
    {
        var length = this.ReadVarInt();

        var list = new List<ArgumentSignature>(length);

        for (int i = 0; i < length; i++)
            list[i] = this.ReadArgumentSignature();

        return list;
    }

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

    public async Task<Vector> ReadAbsolutePositionAsync()
    {
        return new Vector
        {
            X = (int)await ReadDoubleAsync(),
            Y = (int)await ReadDoubleAsync(),
            Z = (int)await ReadDoubleAsync()
        };
    }

    public async Task<Vector> ReadPositionAsync()
    {
        ulong value = await this.ReadUnsignedLongAsync();

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
            X = ReadFloat(),
            Y = ReadFloat(),
            Z = ReadFloat()
        };
    }

    public async Task<VectorF> ReadAbsolutePositionFAsync()
    {
        return new VectorF
        {
            X = (float)await ReadDoubleAsync(),
            Y = (float)await ReadDoubleAsync(),
            Z = (float)await ReadDoubleAsync()
        };
    }

    public async Task<VectorF> ReadPositionFAsync()
    {
        ulong value = await this.ReadUnsignedLongAsync();

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

    [ReadMethod]
    public SoundPosition ReadSoundPosition() => new SoundPosition(this.ReadInt(), this.ReadInt(), this.ReadInt());

    [ReadMethod]
    public Angle ReadAngle() => new Angle(this.ReadUnsignedByte());

    [ReadMethod, DataFormat(typeof(float))]
    public Angle ReadFloatAngle() => ReadFloat();

    public async Task<Angle> ReadAngleAsync() => new Angle(await this.ReadUnsignedByteAsync());

    [ReadMethod]
    public ChatMessage ReadChat()
    {
        var reader = new NbtReader(this);
        var chatMessage = ChatMessage.Empty;

        return !reader.TryReadNextTag<NbtCompound>(out var root) ? chatMessage : chatMessage.FromNbt(root);
    }

    [ReadMethod]
    public byte[] ReadByteArray()
    {
        var length = ReadVarInt();
        return ReadUInt8Array(length);
    }

    [ReadMethod]
    public Guid ReadGuid() =>
        GuidHelper.FromLongs(this.ReadLong(), this.ReadLong());

    [ReadMethod]
    public Guid? ReadOptionalGuid()
    {
        if (this.ReadBoolean())
            return this.ReadGuid();

        return null;
    }

    [ReadMethod]
    public IDictionary<short, ItemStack> ReadSlots()
    {
        var dict = new Dictionary<short, ItemStack>();

        var length = this.ReadVarInt();

        for (int i = 0; i < length; i++)
        {
            var slot = this.ReadShort();
            var item = this.ReadItemStack();

            dict.Add(slot, item);
        }

        return dict;
    }

    [ReadMethod]
    public ItemStack ReadItemStack()
    {
        var count = this.ReadVarInt();

        if (count <= 0)
            return ItemStack.Air;

        var item = ItemsRegistry.Get(this.ReadVarInt());
        var itemStack = new ItemStack(item.Type, count);

        var componentsToAdd = this.ReadVarInt();
        var componentsToRemove = this.ReadVarInt();

        for(int i = 0; i < componentsToAdd; i++)
        {
            var component = ItemComponentsRegistry.Components[this.ReadVarInt()].Invoke();

            component.Read(this);

            itemStack.Add(component.Type, component);
        }

        for (int i = 0; i < componentsToAdd; i++)
            itemStack.Remove((ItemComponentType)this.ReadVarInt());

        return itemStack;
    }

    [ReadMethod]
    public Velocity ReadVelocity()
    {
        return new Velocity(ReadShort(), ReadShort(), ReadShort());
    }
}
