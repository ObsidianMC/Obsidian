using Obsidian.API;
using Obsidian.Boss;
using Obsidian.Chat;
using Obsidian.Commands;
using Obsidian.Entities;
using Obsidian.Items;
using Obsidian.Nbt;
using Obsidian.Nbt.Tags;
using Obsidian.Net.Packets.Play.Client;
using Obsidian.PlayerData.Info;
using Obsidian.Serialization.Attributes;
using Obsidian.Util.Registry.Codecs.Dimensions;
using System;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Net
{
    public partial class MinecraftStream
    {
        [WriteMethod]
        public void WriteByte(sbyte value)
        {
            BaseStream.WriteByte((byte)value);
        }
        
        public async Task WriteByteAsync(sbyte value)
        {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing Byte (0x{value.ToString("X")})");
#endif

            await this.WriteUnsignedByteAsync((byte)value);
        }

        [WriteMethod]
        public void WriteUnsignedByte(byte value)
        {
            BaseStream.WriteByte(value);
        }

        public async Task WriteUnsignedByteAsync(byte value)
        {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing unsigned Byte (0x{value.ToString("X")})");
#endif

            await this.WriteAsync(new[] { value });
        }

        [WriteMethod]
        public void WriteBoolean(bool value)
        {
            BaseStream.WriteByte(value ? 0x01 : 0x00);
        }

        public async Task WriteBooleanAsync(bool value)
        {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing Boolean ({value})");
#endif

            await this.WriteByteAsync((sbyte)(value ? 0x01 : 0x00));
        }

        [WriteMethod]
        public void WriteUnsignedShort(ushort value)
        {
            Span<byte> span = stackalloc byte[2];
            BitConverter.TryWriteBytes(span, value);
            if (BitConverter.IsLittleEndian)
            {
                span.Reverse();
            }
            BaseStream.Write(span);
        }

        public async Task WriteUnsignedShortAsync(ushort value)
        {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing unsigned Short ({value})");
#endif

            var write = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(write);
            }
            await this.WriteAsync(write);
        }

        [WriteMethod]
        public void WriteShort(short value)
        {
            Span<byte> span = stackalloc byte[2];
            BitConverter.TryWriteBytes(span, value);
            if (BitConverter.IsLittleEndian)
            {
                span.Reverse();
            }
            BaseStream.Write(span);
        }

        public async Task WriteShortAsync(short value)
        {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing Short ({value})");
#endif

            var write = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(write);
            }
            await this.WriteAsync(write);
        }

        [WriteMethod]
        public void WriteInt(int value)
        {
            Span<byte> span = stackalloc byte[4];
            BitConverter.TryWriteBytes(span, value);
            if (BitConverter.IsLittleEndian)
            {
                span.Reverse();
            }
            BaseStream.Write(span);
        }

        public async Task WriteIntAsync(int value)
        {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing Int ({value})");
#endif

            var write = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(write);
            }
            await this.WriteAsync(write);
        }

        [WriteMethod]
        public void WriteLong(long value)
        {
            Span<byte> span = stackalloc byte[8];
            BitConverter.TryWriteBytes(span, value);
            if (BitConverter.IsLittleEndian)
            {
                span.Reverse();
            }
            BaseStream.Write(span);
        }

        public async Task WriteLongAsync(long value)
        {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing Long ({value})");
#endif

            var write = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(write);
            }
            await this.WriteAsync(write);
        }

        [WriteMethod]
        public void WriteFloat(float value)
        {
            Span<byte> span = stackalloc byte[4];
            BitConverter.TryWriteBytes(span, value);
            if (BitConverter.IsLittleEndian)
            {
                span.Reverse();
            }
            BaseStream.Write(span);
        }

        public async Task WriteFloatAsync(float value)
        {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing Float ({value})");
#endif

            var write = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(write);
            }
            await this.WriteAsync(write);
        }

        [WriteMethod]
        public void WriteDouble(double value)
        {
            Span<byte> span = stackalloc byte[8];
            BitConverter.TryWriteBytes(span, value);
            if (BitConverter.IsLittleEndian)
            {
                span.Reverse();
            }
            BaseStream.Write(span);
        }

        public async Task WriteDoubleAsync(double value)
        {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing Double ({value})");
#endif

            var write = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(write);
            }
            await this.WriteAsync(write);
        }

        [WriteMethod]
        public void WriteString(string value)
        {
            System.Diagnostics.Debug.Assert(value.Length <= short.MaxValue);
            
            var bytes = Encoding.UTF8.GetBytes(value);
            WriteVarInt(bytes.Length);
            Write(bytes);
        }

        public async Task WriteStringAsync(string value, int maxLength = short.MaxValue)
        {
            //await Globals.PacketLogger.LogDebugAsync($"Writing String ({value})");

            if (value == null)
                throw new ArgumentNullException(nameof(value));

            if (value.Length > maxLength)
                throw new ArgumentException($"string ({value.Length}) exceeded maximum length ({maxLength})", nameof(value));

            var bytes = Encoding.UTF8.GetBytes(value);
            await this.WriteVarIntAsync(bytes.Length);
            await this.WriteAsync(bytes);
        }

        [WriteMethod, VarLength]
        public void WriteVarInt(int value)
        {
            var unsigned = (uint)value;

            do
            {
                var temp = (byte)(unsigned & 127);
                unsigned >>= 7;

                if (unsigned != 0)
                    temp |= 128;

                BaseStream.WriteByte(temp);
            }
            while (unsigned != 0);
        }

        public async Task WriteVarIntAsync(int value)
        {
            //await Globals.PacketLogger.LogDebugAsync($"Writing VarInt ({value})");

            var unsigned = (uint)value;

            do
            {
                var temp = (byte)(unsigned & 127);

                unsigned >>= 7;

                if (unsigned != 0)
                    temp |= 128;

                await this.WriteUnsignedByteAsync(temp);
            }
            while (unsigned != 0);
        }

        public void WriteVarInt(Enum value)
        {
            WriteVarInt(Convert.ToInt32(value));
        }

        /// <summary>
        /// Writes a "VarInt Enum" to the specified <paramref name="stream"/>.
        /// </summary>
        public async Task WriteVarIntAsync(Enum value) => await this.WriteVarIntAsync(Convert.ToInt32(value));

        public async Task WriteLongArrayAsync(long[] values)
        {
            foreach (var value in values)
                await this.WriteLongAsync(value);
        }

        public async Task WriteLongArrayAsync(ulong[] values)
        {
            foreach (var value in values)
                await this.WriteLongAsync((long)value);
        }

        [WriteMethod, VarLength]
        public void WriteVarLong(long value)
        {
            var unsigned = (ulong)value;

            do
            {
                var temp = (byte)(unsigned & 127);

                unsigned >>= 7;

                if (unsigned != 0)
                    temp |= 128;


                BaseStream.WriteByte(temp);
            }
            while (unsigned != 0);
        }

        public async Task WriteVarLongAsync(long value)
        {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing VarLong ({value})");
#endif

            var unsigned = (ulong)value;

            do
            {
                var temp = (byte)(unsigned & 127);

                unsigned >>= 7;

                if (unsigned != 0)
                    temp |= 128;


                await this.WriteUnsignedByteAsync(temp);
            }
            while (unsigned != 0);
        }

        [WriteMethod]
        public void WriteAngle(Angle angle)
        {
            BaseStream.WriteByte(angle.Value);
        }

        public async Task WriteAngleAsync(Angle angle)
        {
            await this.WriteByteAsync((sbyte)angle.Value);
            // await this.WriteUnsignedByteAsync((byte)(angle / Angle.MaxValue * byte.MaxValue));
        }

        [WriteMethod]
        public void WriteChat(ChatMessage chatMessage)
        {
            WriteString(chatMessage.ToString());
        }

        [WriteMethod]
        public void WriteByteArray(byte[] values)
        {
            BaseStream.Write(values);
        }

        [WriteMethod]
        public void WriteUuid(Guid value)
        {
            var uuid = System.Numerics.BigInteger.Parse(value.ToString().Replace("-", ""), System.Globalization.NumberStyles.HexNumber);
            Write(uuid.ToByteArray(false, true));
        }

        [WriteMethod]
        public void WritePosition(Position value)
        {
            var val = (long)(value.X & 0x3FFFFFF) << 38;
            val |= (long)(value.Z & 0x3FFFFFF) << 12;
            val |= (long)(value.Y & 0xFFF);

            WriteLong(val);
        }

        [WriteMethod, Absolute]
        public void WriteAbsolutePosition(Position value)
        {
            WriteDouble(value.X);
            WriteDouble(value.Y);
            WriteDouble(value.Z);
        }

        [WriteMethod]
        public void WritePositionF(PositionF value)
        {
            var val = (long)((int)value.X & 0x3FFFFFF) << 38;
            val |= (long)((int)value.Z & 0x3FFFFFF) << 12;
            val |= (long)((int)value.Y & 0xFFF);

            WriteLong(val);
        }

        [WriteMethod, Absolute]
        public void WriteAbsolutePositionF(PositionF value)
        {
            WriteDouble(value.X);
            WriteDouble(value.Y);
            WriteDouble(value.Z);
        }

        [WriteMethod]
        public void WriteBossBarAction(BossBarAction value)
        {
            WriteVarInt(value.Action);
            Write(value.ToArray());
        }

        [WriteMethod]
        public void WriteTag(Tag value)
        {
            WriteString(value.Name);
            WriteVarInt(value.Count);
            for (int i = 0; i < value.Entries.Count; i++)
            {
                WriteVarInt(value.Entries[i]);
            }
        }

        [WriteMethod]
        public void WriteCommandNode(CommandNode value)
        {
            value.CopyTo(this);
        }

        [WriteMethod]
        public void WriteItemStack(ItemStack value)
        {
            WriteBoolean(value.Present);
            if (value.Present)
            {
                WriteVarInt(value.Id);
                WriteByte((sbyte)value.Count);

                var writer = new NbtWriter(this, string.Empty);
                if (value.Nbt is null)
                {
                    writer.EndCompound();
                    writer.Finish();
                    return;
                }

                writer.WriteShort("id", (short)value.Id);
                writer.WriteInt("Damage", value.Nbt.Damage);
                writer.WriteByte("Count", (byte)value.Count);

                writer.EndCompound();
                writer.Finish();
            }
        }

        [WriteMethod]
        public void WriteEntity(Entity value)
        {
            value.Write(this);
            WriteUnsignedByte(0xff);
        }

        public void WriteEntityMetadataType(byte index, EntityMetadataType entityMetadataType)
        {
            WriteUnsignedByte(index);
            WriteVarInt((int)entityMetadataType);
        }

        [WriteMethod]
        public void WriteVelocity(Velocity value)
        {
            WriteShort(value.X);
            WriteShort(value.Y);
            WriteShort(value.Z);
        }

        [WriteMethod]
        public void WriteMixedCodec(MixedCodec value)
        {
            var dimensions = new NbtCompound(value.Dimensions.Name)
            {
                new NbtString("type", value.Dimensions.Name)
            };

            var list = new NbtList("value", NbtTagType.Compound);

            foreach (var (_, codec) in value.Dimensions)
            {
                codec.Write(list);
            }

            dimensions.Add(list);

            #region biomes
            var biomeCompound = new NbtCompound(value.Biomes.Name)
            {
                new NbtString("type", value.Biomes.Name)
            };

            var biomes = new NbtList("value", NbtTagType.Compound);

            foreach (var (_, biome) in value.Biomes)
            {
                biome.Write(biomes);
            }

            biomeCompound.Add(biomes);
            #endregion

            var compound = new NbtCompound(string.Empty)
            {
                dimensions,
                biomeCompound
            };
            var nbt = new NbtFile(compound);

            nbt.SaveToStream(this, NbtCompression.None);
        }

        [WriteMethod]
        public void WriteDimensionCodec(DimensionCodec value)
        {
            var nbt = new NbtFile(value.ToNbt());
            nbt.SaveToStream(this, NbtCompression.None);
        }

        [WriteMethod]
        public void WriteSoundPosition(SoundPosition value)
        {
            WriteInt(value.X);
            WriteInt(value.Y);
            WriteInt(value.Z);
        }

        [WriteMethod]
        public void WritePlayerInfoAction(PlayerInfoAction value)
        {
            value.Write(this);
        }

        public async Task WriteEntityMetdata(byte index, EntityMetadataType type, object value, bool optional = false)
        {
            await this.WriteUnsignedByteAsync(index);
            await this.WriteVarIntAsync((int)type);
            switch (type)
            {
                case EntityMetadataType.Byte:
                    await this.WriteUnsignedByteAsync((byte)value);
                    break;

                case EntityMetadataType.VarInt:
                    await this.WriteVarIntAsync((int)value);
                    break;

                case EntityMetadataType.Float:
                    await this.WriteFloatAsync((float)value);
                    break;

                case EntityMetadataType.String:
                    await this.WriteStringAsync((string)value);
                    break;

                case EntityMetadataType.Chat:
                    await this.WriteChatAsync((ChatMessage)value);
                    break;

                case EntityMetadataType.OptChat:
                    await this.WriteBooleanAsync(optional);

                    if (optional)
                        await this.WriteChatAsync((ChatMessage)value);
                    break;

                case EntityMetadataType.Slot:
                    await this.WriteSlotAsync((ItemStack)value);
                    break;

                case EntityMetadataType.Boolean:
                    await this.WriteBooleanAsync((bool)value);
                    break;

                case EntityMetadataType.Rotation:
                    break;

                case EntityMetadataType.Position:
                    await this.WritePositionFAsync((PositionF)value);
                    break;

                case EntityMetadataType.OptPosition:
                    await this.WriteBooleanAsync(optional);

                    if (optional)
                        await this.WritePositionFAsync((PositionF)value);

                    break;

                case EntityMetadataType.Direction:
                    break;

                case EntityMetadataType.OptUuid:
                    await this.WriteBooleanAsync(optional);

                    if (optional)
                        await this.WriteUuidAsync((Guid)value);
                    break;

                case EntityMetadataType.OptBlockId:
                    await this.WriteVarIntAsync((int)value);
                    break;

                case EntityMetadataType.Nbt:
                case EntityMetadataType.Particle:
                case EntityMetadataType.VillagerData:
                case EntityMetadataType.OptVarInt:
                    if (optional)
                    {
                        await this.WriteVarIntAsync(0);
                        break;
                    }
                    await this.WriteVarIntAsync(1 + (int)value);
                    break;
                case EntityMetadataType.Pose:
                    await this.WriteVarIntAsync((Pose)value);
                    break;
                default:
                    break;
            }
        }

        public async Task WriteUuidAsync(Guid value)
        {
            //var arr = value.ToByteArray();
            BigInteger uuid = BigInteger.Parse(value.ToString().Replace("-", ""), System.Globalization.NumberStyles.HexNumber);
            await this.WriteAsync(uuid.ToByteArray(false, true));
        }

        public async Task WriteChatAsync(ChatMessage value) => await this.WriteStringAsync(value.ToString());

        public async Task WritePositionAsync(Position value)
        {
            var val = (long)(value.X & 0x3FFFFFF) << 38;
            val |= (long)(value.Z & 0x3FFFFFF) << 12;
            val |= (long)(value.Y & 0xFFF);

            await this.WriteLongAsync(val);
        }

        public async Task WritePositionFAsync(PositionF value)
        {
            var val = (long)((int)value.X & 0x3FFFFFF) << 38;
            val |= (long)((int)value.Z & 0x3FFFFFF) << 12;
            val |= (long)((int)value.Y & 0xFFF);

            await this.WriteLongAsync(val);
        }

        public async Task WriteSlotAsync(ItemStack slot)
        {
            await this.WriteBooleanAsync(slot.Present);
            if (slot.Present)
            {
                await this.WriteVarIntAsync(slot.Id);
                await this.WriteByteAsync((sbyte)slot.Count);

                var writer = new NbtWriter(this, "");
                if (slot.Nbt == null)
                {
                    writer.EndCompound();
                    writer.Finish();
                    return;
                }

                //TODO write enchants
                writer.WriteShort("id", (short)slot.Id);
                writer.WriteInt("Damage", slot.Nbt.Damage);
                writer.WriteByte("Count", (byte)slot.Count);

                writer.EndCompound();

                writer.Finish();
            }
        }
    }
}
