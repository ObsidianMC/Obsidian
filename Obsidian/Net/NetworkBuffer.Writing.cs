using Obsidian.API.Advancements;
using Obsidian.API.Inventory;
using Obsidian.API.Registry.Codecs.ArmorTrims.TrimMaterial;
using Obsidian.API.Registry.Codecs.ArmorTrims.TrimPattern;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.Registry.Codecs.Chat;
using Obsidian.API.Registry.Codecs.DamageTypes;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.API.Registry.Codecs.PaintingVariant;
using Obsidian.API.Registry.Codecs.WolfVariant;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Obsidian.Net;
public partial class NetworkBuffer : INetStreamWriter
{
    public bool CanWrite { get; set; }

    #region Generic Writing
    public unsafe void WriteBoolean(bool value) => this.WriteByte(*(byte*)&value);
    public void WriteByte(sbyte value) => this.WriteByte((byte)value);
    public void WriteByte(Enum value) => this.WriteByte((byte)value.GetHashCode());

    [WriteMethod]
    public void WriteInt(int value)
    {
        Span<byte> span = stackalloc byte[IntSize];
        BinaryPrimitives.WriteInt32BigEndian(span, value);
        this.Write(span);
    }

    public void WriteInt(Enum value) => this.WriteInt(value.GetHashCode());

    [WriteMethod]
    public void WriteShort(short value)
    {
        Span<byte> span = stackalloc byte[ShortSize];
        BinaryPrimitives.WriteInt16BigEndian(span, value);
        this.Write(span);
    }

    [WriteMethod]
    public void WriteUnsignedShort(ushort value)
    {
        Span<byte> span = stackalloc byte[ShortSize];
        BinaryPrimitives.WriteUInt16BigEndian(span, value);
        this.Write(span);
    }

    [WriteMethod]
    public void WriteLong(long value)
    {
        Span<byte> span = stackalloc byte[LongSize];
        BinaryPrimitives.WriteInt64BigEndian(span, value);
        this.Write(span);
    }

    [WriteMethod]
    public void WriteSingle(float value)
    {
        Span<byte> span = stackalloc byte[IntSize];
        BinaryPrimitives.WriteSingleBigEndian(span, value);
        this.Write(span);
    }

    [WriteMethod]
    public void WriteDouble(double value)
    {
        Span<byte> span = stackalloc byte[LongSize];
        BinaryPrimitives.WriteDoubleBigEndian(span, value);
        this.Write(span);
    }

    [WriteMethod]
    public void WriteString(string value, int maxLength = short.MaxValue)
    {
        Debug.Assert(value.Length <= maxLength);

        using var bytes = new RentedArray<byte>(Encoding.UTF8.GetByteCount(value));
        Encoding.UTF8.GetBytes(value, bytes.Span);
        WriteVarInt(bytes.Length);

        if (bytes.Length == 0)
            return;

        Write(bytes);
    }

    [WriteMethod]
    public void WriteUuid(Guid value)
    {
        if (value == Guid.Empty)
        {
            WriteLong(0L);
            WriteLong(0L);
        }
        else
        {
            var uuid = System.Numerics.BigInteger.Parse(value.ToString().Replace("-", ""), System.Globalization.NumberStyles.HexNumber);
            Write(uuid.ToByteArray(false, true));
        }
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

            this.WriteByte(temp);
        }
        while (unsigned != 0);
    }

    public void WriteVarInt(Enum value) => WriteVarInt(value.GetHashCode());

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


            this.WriteByte(temp);
        }
        while (unsigned != 0);
    }

    public void WriteLongArray(long[] values)
    {
        Span<byte> buffer = stackalloc byte[LongSize];
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteInt64BigEndian(buffer, values[i]);
            this.Write(buffer);
        }
    }

    [WriteMethod]
    public void WriteByteArray(byte[] values) => this.Write(values);

    [WriteMethod]
    public void WriteByteArray(Span<byte> values) => this.Write(values);

    #endregion

    public void WritePosition(SoundPosition position)
    {
        this.WriteInt(position.X);
        this.WriteInt(position.Y);
        this.WriteInt(position.Z);
    }

    [WriteMethod]
    public void WritePosition(Vector value)
    {
        var val = (long)(value.X & 0x3FFFFFF) << 38;
        val |= (long)(value.Z & 0x3FFFFFF) << 12;
        val |= (long)(value.Y & 0xFFF);

        WriteLong(val);
    }

    [WriteMethod, DataFormat(typeof(double))]
    public void WriteAbsolutePosition(Vector value)
    {
        WriteDouble(value.X);
        WriteDouble(value.Y);
        WriteDouble(value.Z);
    }

    [WriteMethod, DataFormat(typeof(float))]
    public void WriteAbsoluteFloatPosition(Vector value)
    {
        WriteSingle(value.X);
        WriteSingle(value.Y);
        WriteSingle(value.Z);
    }

    [WriteMethod, DataFormat(typeof(short))]
    public void WriteAbsoluteShortPosition(Vector value)
    {
        WriteShort((short)value.X);
        WriteShort((short)value.Y);
        WriteShort((short)value.Z);
    }

    [WriteMethod, DataFormat(typeof(short))]
    public void WriteAbsoluteShortPosition(VectorF value)
    {
        WriteShort((short)value.X);
        WriteShort((short)value.Y);
        WriteShort((short)value.Z);
    }

    [WriteMethod]
    public void WritePositionF(VectorF value)
    {
        var val = (long)((int)value.X & 0x3FFFFFF) << 38;
        val |= (long)((int)value.Z & 0x3FFFFFF) << 12;
        val |= (long)((int)value.Y & 0xFFF);

        WriteLong(val);
    }

    [WriteMethod, DataFormat(typeof(double))]
    public void WriteAbsolutePositionF(VectorF value)
    {
        WriteDouble(value.X);
        WriteDouble(value.Y);
        WriteDouble(value.Z);
    }

    [WriteMethod, DataFormat(typeof(float))]
    public void WriteAbsoluteFloatPositionF(VectorF value)
    {
        WriteSingle(value.X);
        WriteSingle(value.Y);
        WriteSingle(value.Z);
    }

    public void WriteAdvancements(IDictionary<string, Advancement> advancements)
    {
        this.WriteVarInt(advancements.Count);

        foreach (var (name, value) in advancements)
        {
            this.WriteString(name);
            this.WriteAdvancement(value);
        }
    }

    //TODO make sure this is up to date
    public void WriteAdvancement(Advancement advancement)
    {
        var hasParent = !string.IsNullOrEmpty(advancement.Parent);
        this.WriteBoolean(hasParent);

        if (hasParent)
            this.WriteString(advancement.Parent);

        var hasDisplay = advancement.Display != null;

        this.WriteBoolean(hasDisplay);

        if (hasDisplay)
        {
            this.WriteChat(advancement.Display.Title);
            this.WriteChat(advancement.Display.Description);

            this.WriteItemStack(ItemsRegistry.GetSingleItem(advancement.Display.Icon.Type));

            this.WriteVarInt(advancement.Display.AdvancementFrameType);

            this.WriteInt((int)advancement.Display.Flags);

            if (advancement.Display.Flags.HasFlag(AdvancementFlags.HasBackgroundTexture))
                this.WriteString(advancement.Display.BackgroundTexture);

            this.WriteSingle(advancement.Display.XCoord);
            this.WriteSingle(advancement.Display.YCoord);
        }

        this.WriteVarInt(advancement.Criteria.Count);

        foreach (var criteria in advancement.Criteria)
            this.WriteString(criteria.Identifier);

        var reqired = advancement.Criteria.Where(x => x.Required);

        //For some reason this takes a array of an array??
        if (reqired.Any())
        {
            //Always gonna be 1 for now
            this.WriteVarInt(1);

            this.WriteVarInt(reqired.Count());

            foreach (var criteria in reqired)
                this.WriteString(criteria.Identifier);
        }
    }

    public void WriteAttributeModifier(AttributeModifier attribute) => AttributeModifier.Write(attribute, this);

    [WriteMethod]
    public void WriteBitSet(BitSet bitset, bool isFixed = false)
    {
        //TODO WE HAVE TO DO SOMETHING ABOUT THIS
        if (isFixed)
        {
            this.WriteByte((byte)bitset.DataStorage.Span[0]);
            return;
        }

        this.WriteVarInt(bitset.DataStorage.Length);
        if (bitset.DataStorage.Length > 0)
            this.WriteLongArray(bitset.DataStorage.ToArray());
    }

    [WriteMethod]
    public void WriteChat(ChatMessage chatMessage)
    {
        if (chatMessage == null)
            return;

        using var writer = new RawNbtWriter(true);

        writer.WriteChatMessage(chatMessage);

        writer.EndCompound();
        writer.TryFinish();

        this.Write(writer.Data);
    }

    [WriteMethod]
    public void WriteCodec(ICodec codec)
    {
        using var writer = new RawNbtWriter(true);

        codec.WriteElement(writer);

        if (codec is DimensionCodec dim)
            dim.WriteElement(writer);
        else if (codec is BiomeCodec biome)
            biome.WriteElement(writer);
        else if (codec is ChatTypeCodec chat)
            chat.WriteElement(writer);
        else if (codec is TrimPatternCodec trimPattern)
            trimPattern.WriteElement(writer);
        else if (codec is TrimMaterialCodec trimMaterial)
            trimMaterial.WriteElement(writer);
        else if (codec is DamageTypeCodec damageType)
            damageType.WriteElement(writer);
        else if (codec is WolfVariantCodec wolfVariant)
            wolfVariant.WriteElement(writer);
        else if (codec is PaintingVariantCodec paintingVariant)
            paintingVariant.WriteElement(writer);

        writer.EndCompound();
        writer.TryFinish();

        this.Write(writer.Data);
    }

    [WriteMethod]
    public void WriteDateTimeOffset(DateTimeOffset date) => this.WriteLong(date.ToUnixTimeMilliseconds());

    public void WriteEnchantment(Enchantment enchantment)
    {
        this.WriteVarInt(enchantment.Id);
        this.WriteVarInt(enchantment.Level);
    }

    public void WriteEntity(IEntity entity)
    {
        entity.Write(this);
        WriteByte(0xff);
    }

    public void WriteEntityMetadataType(byte index, EntityMetadataType entityMetadataType)
    {
        WriteByte(index);
        WriteVarInt((int)entityMetadataType);
    }

    [WriteMethod]
    public void WriteItemStack(ItemStack? value)
    {
        value ??= ItemStack.Air;

        var item = value.AsItem();

        WriteVarInt(value.Count);

        if (value.Count <= 0)
            return;

        WriteVarInt(item.Id);
        WriteVarInt(value.TotalComponents);
        WriteVarInt(value.RemoveComponents.Count);

        foreach (var component in value)
        {
            this.WriteVarInt(component.Type);
            component.Write(this);
        }

        foreach (var componentType in value.RemoveComponents)
            this.WriteVarInt(componentType);
    }

    public void WriteLengthPrefixedArray<TValue>(Action<TValue> write, params List<TValue> values)
    {
        this.WriteVarInt(values.Count);

        foreach (var value in values)
            write(value);
    }

    public void WriteLengthPrefixedArray(bool showInTooltips, params List<Enchantment> enchantments)
    {
        this.WriteVarInt(enchantments.Count);

        foreach (var enchantment in enchantments)
            this.WriteEnchantment(enchantment);

        this.WriteBoolean(showInTooltips);
    }

    /// <summary>
    /// Writes a full packet to the stream. Note that this method should NOT be an alternative to
    /// <see cref="WriteCompressedPacket"/> for packets whose size is less than the compression threshold.
    /// </summary>
    /// <param name="packet">The packet to write.</param>
    public void WritePacket(IClientboundPacket packet)
    {
        if (packet is BundledPacket bp)
        {
            // Wrap the bundle with delimiter packets and writes all content packets
            this.WritePacket(new BundleDelimiterPacket());
            foreach (var p in bp.Packets)
                this.WritePacket(p);
            this.WritePacket(new BundleDelimiterPacket());
            return;
        }

        using var packetStream = new NetworkBuffer();

        packet.Serialize(packetStream);

        this.WriteVarInt(packetStream.Size + packet.Id.GetVarIntLength());
        this.WriteVarInt(packet.Id);

        this.Write(packetStream);
    }
    
    private static readonly BundleDelimiterPacket delimiterPacket = new BundleDelimiterPacket();

    /// <summary>
    /// Writes a full packet to the stream with compression enabled. Note that this method is NOT
    /// equivalent to <see cref="WritePacket"/> even if the packet size is less than <paramref name="compressionThreshold"/>.
    /// See <see href="https://minecraft.wiki/w/Minecraft_Wiki:Projects/wiki.vg_merge/Protocol#With_compression"/>.
    /// </summary>
    /// <param name="packet">The packet to write.</param>
    /// <param name="compressionThreshold">The threshold of packet size at which to compress the packet.</param>
    public void WriteCompressedPacket(IClientboundPacket packet, int compressionThreshold)
    {
        if (packet is BundledPacket bp)
        {
            // Wrap the bundle with delimiter packets and writes all content packets
            this.WriteCompressedPacket(delimiterPacket, compressionThreshold);
            foreach (var p in bp.Packets)
                this.WriteCompressedPacket(p, compressionThreshold);
            this.WriteCompressedPacket(delimiterPacket, compressionThreshold);
            return;
        }

        using var networkBuffer = new NetworkBuffer();

        networkBuffer.WriteVarInt(packet.Id);
        packet.Serialize(networkBuffer);

        var dataLength = (int)networkBuffer.Offset;

        if (dataLength >= compressionThreshold)
        {   // Compress the packet
            using NetworkBuffer compressedBuffer = new();

            using var ms = new MemoryStream();
            using (ZLibStream zlibStream = new(ms, CompressionLevel.Optimal, true))
            {
                zlibStream.Write(networkBuffer.AsSpan(offset: 0));
            }

            var data = ArrayPool<byte>.Shared.Rent(dataLength);
            ms.Position = 0;

            ms.ReadExactly(data);

            compressedBuffer.Write(data.AsSpan(0, dataLength));

            ArrayPool<byte>.Shared.Return(data);

            int totalLength = dataLength.GetVarIntLength() + (int)compressedBuffer.Offset;

            this.WriteVarInt(totalLength);
            this.WriteVarInt(dataLength);

            this.Write(compressedBuffer);
        }
        else
        {   // Do not compress the packet
            int totalLength = dataLength + 1;

            // same as WritePacket but insert a 0 after length
            this.WriteVarInt(totalLength);
            this.WriteVarInt(0);
            this.Write(networkBuffer);
        }
    }

    [WriteMethod]
    public void WriteSkinProperty(SkinProperty skinProperty)
    {
        this.WriteString(skinProperty.Name);
        this.WriteString(skinProperty.Value);

        var signed = !string.IsNullOrWhiteSpace(skinProperty.Signature);

        this.WriteBoolean(signed);
        if (signed)
            this.WriteString(skinProperty.Signature);
    }

    [WriteMethod]
    public void WriteSoundEffect(SoundEffect sound)
    {
        this.WriteString(JsonNamingPolicy.SnakeCaseLower.ConvertName(sound.SoundName ?? sound.SoundId.ToString()));

        if (sound.FixedRange.HasValue)
            this.WriteSingle(sound.FixedRange.Value);
    }

    public void WriteSoundEvent(SoundEvent soundEvent)
    {
        this.WriteString(soundEvent.ResourceLocation);
        this.WriteOptional(soundEvent.FixedRange);
    }


    [WriteMethod]
    public void WriteVelocity(Velocity value)
    {
        WriteShort(value.X);
        WriteShort(value.Y);
        WriteShort(value.Z);
    }

    #region optionals
    private bool ShouldWriteOptional<TValue>(TValue? value)
    {
        var notNull = value != null;

        this.WriteBoolean(notNull);

        return notNull;
    }

    public void WriteOptional(Enum? value)
    {
        if (!this.ShouldWriteOptional(value))
            return;

        this.WriteVarInt(value!.GetHashCode());
    }

    public void WriteOptional(int? value)
    {
        if (!this.ShouldWriteOptional(value))
            return;

        this.WriteInt(value!.Value);
    }

    public void WriteOptional(double? value)
    {
        if (!this.ShouldWriteOptional(value))
            return;

        this.WriteDouble(value!.Value);
    }

    public void WriteOptional(short? value)
    {
        if (!this.ShouldWriteOptional(value))
            return;

        this.WriteShort(value!.Value);
    }

    public void WriteOptional(Guid? value)
    {
        if (!this.ShouldWriteOptional(value))
            return;

        this.WriteUuid(value!.Value);
    }

    public void WriteOptional(float? value)
    {
        if (!this.ShouldWriteOptional(value))
            return;

        this.WriteSingle(value!.Value);
    }

    public void WriteOptional(string? value)
    {
        if (!this.ShouldWriteOptional(value))
            return;

        this.WriteString(value!);
    }

    public void WriteOptional(bool? value)
    {
        if (!this.ShouldWriteOptional(value))
            return;

        this.WriteBoolean(value!.Value);
    }

    public void WriteOptional(byte? value)
    {
        if (!this.ShouldWriteOptional(value))
            return;

        this.WriteByte(value!.Value);
    }

    public void WriteOptional<TValue>(TValue? optional) where TValue : struct, INetworkSerializable<TValue>
    {
        this.WriteBoolean(optional.HasValue);

        if (optional.HasValue)
            TValue.Write(optional.Value, this);
    }

    public void WriteOptional<TValue>(TValue? value) where TValue : INetworkSerializable<TValue>
    {
        var shouldWrite = this.ShouldWriteOptional(value);
        if (!shouldWrite)
            return;

        TValue.Write(value!, this);
    }
    #endregion

    public byte[] ToArray() => this.Data;
}
