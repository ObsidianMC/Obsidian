﻿using Newtonsoft.Json;
using Obsidian.API;
using Obsidian.Chat;
using Obsidian.Nbt;
using Obsidian.Serialization.Attributes;
using Obsidian.Utilities;
using Obsidian.Utilities.Registry;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Net
{
    public partial class MinecraftStream
    {

        [ReadMethod]
        public sbyte ReadSignedByte() => (sbyte)this.ReadUnsignedByte();

        public async Task<sbyte> ReadByteAsync() => (sbyte)await this.ReadUnsignedByteAsync();

        [ReadMethod]
        public byte ReadUnsignedByte()
        {
            Span<byte> buffer = stackalloc byte[1];
            BaseStream.Read(buffer);
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
            if (value == 0x00)
            {
                return false;
            }
            else if (value == 0x01)
            {
                return true;
            }
            else
            {
                throw new ArgumentOutOfRangeException("Byte returned by stream is out of range (0x00 or 0x01)", nameof(BaseStream));
            }
        }

        [ReadMethod]
        public ushort ReadUnsignedShort()
        {
            Span<byte> buffer = stackalloc byte[2];
            this.Read(buffer);
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
            this.Read(buffer);
            return BinaryPrimitives.ReadInt16BigEndian(buffer);
        }

        public async Task<short> ReadShortAsync()
        {
            var buffer = new byte[2];
            await this.ReadAsync(buffer);
            return BinaryPrimitives.ReadInt16BigEndian(buffer);
        }

        [ReadMethod]
        public int ReadInt()
        {
            Span<byte> buffer = stackalloc byte[4];
            this.Read(buffer);
            return BinaryPrimitives.ReadInt32BigEndian(buffer);
        }

        public async Task<int> ReadIntAsync()
        {
            var buffer = new byte[4];
            await this.ReadAsync(buffer);
            return BinaryPrimitives.ReadInt32BigEndian(buffer);
        }

        [ReadMethod]
        public long ReadLong()
        {
            Span<byte> buffer = stackalloc byte[8];
            this.Read(buffer);
            return BinaryPrimitives.ReadInt64BigEndian(buffer);
        }

        public async Task<long> ReadLongAsync()
        {
            var buffer = new byte[8];
            await this.ReadAsync(buffer);
            return BinaryPrimitives.ReadInt64BigEndian(buffer);
        }

        [ReadMethod]
        public ulong ReadUnsignedLong()
        {
            Span<byte> buffer = stackalloc byte[8];
            this.Read(buffer);
            return BinaryPrimitives.ReadUInt64BigEndian(buffer);
        }

        public async Task<ulong> ReadUnsignedLongAsync()
        {
            var buffer = new byte[8];
            await this.ReadAsync(buffer);
            return BinaryPrimitives.ReadUInt64BigEndian(buffer);
        }

        [ReadMethod]
        public float ReadFloat()
        {
            Span<byte> buffer = stackalloc byte[4];
            this.Read(buffer);
            return BinaryPrimitives.ReadSingleBigEndian(buffer);
        }

        public async Task<float> ReadFloatAsync()
        {
            var buffer = new byte[4];
            await this.ReadAsync(buffer);
            return BinaryPrimitives.ReadSingleBigEndian(buffer);
        }

        [ReadMethod]
        public double ReadDouble()
        {
            Span<byte> buffer = stackalloc byte[8];
            this.Read(buffer);
            return BinaryPrimitives.ReadDoubleBigEndian(buffer);
        }

        public async Task<double> ReadDoubleAsync()
        {
            var buffer = new byte[8];
            await this.ReadAsync(buffer);
            return BinaryPrimitives.ReadDoubleBigEndian(buffer);
        }

        [ReadMethod]
        public string ReadString(int maxLength = 32767)
        {
            var length = ReadVarInt();
            var buffer = new byte[length];
            this.Read(buffer, 0, length);

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
            var buffer = new byte[length];
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            await this.ReadAsync(buffer, 0, length);

            var value = Encoding.UTF8.GetString(buffer);
            if (maxLength > 0 && value.Length > maxLength)
            {
                throw new ArgumentException($"string ({value.Length}) exceeded maximum length ({maxLength})", nameof(value));
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
                X = (float) await ReadDoubleAsync(),
                Y = (float) await ReadDoubleAsync(),
                Z = (float) await ReadDoubleAsync()
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
            string value = ReadString();
            return JsonConvert.DeserializeObject<ChatMessage>(value);
        }

        [ReadMethod]
        public byte[] ReadByteArray()
        {
            var length = ReadVarInt();
            return ReadUInt8Array(length);
        }

        [ReadMethod]
        public Guid ReadGuid()
        {
            return Guid.Parse(ReadString());
        }

        [ReadMethod]
        public ItemStack ReadItemStack()
        {
            var present = ReadBoolean();

            if (present)
            {
                var item = Registry.GetItem((short)ReadVarInt());

                var slot = new ItemStack(item.Type, ReadUnsignedByte())
                {
                    Present = present
                };

                var reader = new NbtReader(this);

                INbtTag tag;
                while ((tag = reader.ReadNextTag()) != null)
                {
                    var itemMetaBuilder = new ItemMetaBuilder();

                    if (tag is NbtCompound root)
                    {
                        foreach (var (name, child) in root)
                        {
                            switch (name.ToUpperInvariant())
                            {
                                case "ENCHANTMENTS":
                                    {
                                        var enchantments = (NbtList)child;

                                        foreach (var enchant in enchantments)
                                        {
                                            if (enchant is NbtCompound compound)
                                            {

                                                itemMetaBuilder.AddEnchantment(compound.GetString("id").ToEnchantType(), compound.GetShort("lvl"));
                                            }
                                        }

                                        break;
                                    }

                                case "STOREDENCHANTMENTS":
                                    {
                                        var enchantments = (NbtList)child;

                                        //Globals.PacketLogger.LogDebug($"List Type: {enchantments.ListType}");

                                        foreach (var enchantment in enchantments)
                                        {
                                            if (enchantment is NbtCompound compound)
                                            {
                                                compound.TryGetTag("id", out var id);
                                                compound.TryGetTag("lvl", out var lvl);

                                                itemMetaBuilder.AddStoredEnchantment(compound.GetString("id").ToEnchantType(), compound.GetShort("lvl"));
                                            }
                                        }
                                        break;
                                    }

                                case "SLOT":
                                    {
                                        var byteTag = (NbtTag<byte>)child;

                                        itemMetaBuilder.WithSlot(byteTag.Value);
                                        //Console.WriteLine($"Setting slot: {itemMetaBuilder.Slot}");
                                        break;
                                    }

                                case "DAMAGE":
                                    {
                                        var intTag = (NbtTag<int>)child;

                                        itemMetaBuilder.WithDurability(intTag.Value);
                                        //Globals.PacketLogger.LogDebug($"Setting damage: {tag.IntValue}");
                                        break;
                                    }

                                case "DISPLAY":
                                    {
                                        var display = (NbtCompound)child;

                                        foreach (var (displayTagName, displayTag) in display)
                                        {
                                            if (displayTagName.EqualsIgnoreCase("name") && displayTag is NbtTag<string> stringTag)
                                            {
                                                itemMetaBuilder.WithName(stringTag.Value);
                                            }
                                            else if (displayTag.Name.EqualsIgnoreCase("lore"))
                                            {
                                                var loreTag = (NbtList)displayTag;

                                                foreach (NbtTag<string> lore in loreTag)
                                                    itemMetaBuilder.AddLore(JsonConvert.DeserializeObject<ChatMessage>(lore.Value));
                                            }
                                        }
                                        break;
                                    }
                            }
                        }
                    }

                    slot.ItemMeta = itemMetaBuilder.Build();
                }

                return slot;
            }

            return null;
        }

        public async Task<ItemStack> ReadSlotAsync()
        {
            var present = await this.ReadBooleanAsync();

            if (present)
            {
                var item = Registry.GetItem((short)await this.ReadVarIntAsync());

                var slot = new ItemStack(item.Type, await this.ReadByteAsync())
                {
                    Present = present
                };

                var reader = new NbtReader(this);

                INbtTag tag;
                while ((tag = reader.ReadNextTag()) != null)
                {
                    var itemMetaBuilder = new ItemMetaBuilder();

                    if (tag is NbtCompound root)
                    {
     
                        foreach (var (name, child) in root)
                        {
                            switch (name.ToLower())
                            {
                                case "enchantments":
                                    {
                                        var enchantments = (NbtList)child;

                                        foreach (var enchant in enchantments)
                                        {
                                            if (enchant is NbtCompound compound)
                                            {
                                                itemMetaBuilder.AddEnchantment(compound.GetString("id").ToEnchantType(), compound.GetShort("lvl"));
                                            }
                                        }

                                        break;
                                    }
                                case "storedenchantments":
                                    {
                                        var enchantments = (NbtList)child;

                                        //Globals.PacketLogger.LogDebug($"List Type: {enchantments.ListType}");

                                        foreach (var enchantment in enchantments)
                                        {
                                            if (enchantment is NbtCompound compound)
                                            {
                                                compound.TryGetTag("id", out var id);
                                                compound.TryGetTag("lvl", out var lvl);

                                                itemMetaBuilder.AddStoredEnchantment(compound.GetString("id").ToEnchantType(), compound.GetShort("lvl"));
                                            }
                                        }
                                        break;
                                    }
                                case "slot":
                                    {
                                        var byteTag = (NbtTag<byte>)child;

                                        itemMetaBuilder.WithSlot(byteTag.Value);
                                        //Console.WriteLine($"Setting slot: {itemMetaBuilder.Slot}");
                                        break;
                                    }
                                case "damage":
                                    {

                                        var intTag = (NbtTag<int>)child;

                                        itemMetaBuilder.WithDurability(intTag.Value);
                                        //Globals.PacketLogger.LogDebug($"Setting damage: {tag.IntValue}");
                                        break;
                                    }
                                case "display":
                                    {
                                        var display = (NbtCompound)child;

                                        foreach (var (displayTagName, displayTag) in display)
                                        {
                                            if (displayTagName.EqualsIgnoreCase("name") && displayTag is NbtTag<string> stringTag)
                                            {
                                                var messages = JsonConvert.DeserializeObject<ChatMessage[]>(stringTag.Value);
                                                itemMetaBuilder.WithName(messages[0]);
                                            }
                                            else if (displayTag.Name.EqualsIgnoreCase("lore"))
                                            {
                                                var loreTag = (NbtList)displayTag;

                                                foreach (NbtTag<string> lore in loreTag)
                                                    itemMetaBuilder.AddLore(JsonConvert.DeserializeObject<ChatMessage>(lore.Value));
                                            }
                                        }
                                        break;
                                    }
                                default:
                                    break;
                            }
                        }
                        //slot.ItemNbt.Slot = compound.Get<NbtByte>("Slot").Value;
                        //slot.ItemNbt.Count = compound.Get<NbtByte>("Count").Value;
                        //slot.ItemNbt.Id = compound.Get<NbtShort>("id").Value;
                        //slot.ItemNbt.Damage = compound.Get<NbtShort>("Damage").Value;
                        //slot.ItemNbt.RepairCost = compound.Get<NbtInt>("RepairCost").Value;
                    }

                    slot.ItemMeta = itemMetaBuilder.Build();
                }

                return slot;
            }

            return null;
        }

        [ReadMethod]
        public Velocity ReadVelocity()
        {
            return new Velocity(ReadShort(), ReadShort(), ReadShort());
        }
    }
}
