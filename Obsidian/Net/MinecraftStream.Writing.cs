using Microsoft.CodeAnalysis;
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
using Obsidian.Commands;
using Obsidian.Nbt;
using Obsidian.Net.Actions.BossBar;
using Obsidian.Net.Actions.PlayerInfo;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.WindowProperties;
using Obsidian.Serialization.Attributes;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Obsidian.Net;

public partial class MinecraftStream : INetStreamWriter
{
    [WriteMethod]
    public void WriteByte(sbyte value)
    {
        WriteByte((byte)value);
    }

    public void WriteByte(Enum value)
    {
        WriteByte((byte)value.GetHashCode());
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

        using MinecraftStream dataStream = new();
        dataStream.WriteVarInt(packet.Id);
        packet.Serialize(dataStream);
        int length = (int)dataStream.Length;

        this.Lock.Wait();

        this.WriteVarInt(length);
        dataStream.Position = 0;
        dataStream.CopyTo(this);

        this.Lock.Release();
    }

    public void WriteAttributeModifier(AttributeModifier attributeModifier) =>
        AttributeModifier.Write(attributeModifier, this);

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
            this.WriteCompressedPacket(new BundleDelimiterPacket(), compressionThreshold);
            foreach (var p in bp.Packets)
                this.WriteCompressedPacket(p, compressionThreshold);
            this.WriteCompressedPacket(new BundleDelimiterPacket(), compressionThreshold);
            return;
        }

        using MinecraftStream dataStream = new();
        dataStream.WriteVarInt(packet.Id);
        packet.Serialize(dataStream);
        int dataLength = (int)dataStream.Length;

        if (dataLength >= compressionThreshold)
        {   // Compress the packet
            using MinecraftStream compressedStream = new();
            using (ZLibStream zlibStream = new(compressedStream, CompressionLevel.Optimal, true))
            {
                zlibStream.Write(dataStream.ToArray());
            }
            int totalLength = dataLength.GetVarIntLength() + (int)compressedStream.Length;

            this.Lock.Wait();

            this.WriteVarInt(totalLength);
            this.WriteVarInt(dataLength);
            compressedStream.Position = 0;
            compressedStream.CopyTo(this);

            this.Lock.Release();
        }
        else
        {   // Do not compress the packet
            int totalLength = dataLength + 1;

            this.Lock.Wait();

            // same as WritePacket but insert a 0 after length
            this.WriteVarInt(totalLength);
            this.WriteVarInt(0);
            dataStream.Position = 0;
            dataStream.CopyTo(this);

            this.Lock.Release();
        }
    }

    public void WriteLengthPrefixedArray(params List<ChatMessage> textComponents)
    {
        this.WriteVarInt(textComponents.Count);

        foreach (var component in textComponents)
            this.WriteChat(component);
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

    public void WriteEnchantment(Enchantment enchantment)
    {
        this.WriteVarInt(enchantment.Id);
        this.WriteVarInt(enchantment.Level);
    }

    public async Task WriteByteAsync(sbyte value)
    {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing Byte (0x{value.ToString("X")})");
#endif

        await WriteUnsignedByteAsync((byte)value);
    }

    public async Task WriteUnsignedByteAsync(byte value)
    {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing unsigned Byte (0x{value.ToString("X")})");
#endif

        await WriteAsync([value]);
    }

    [WriteMethod]
    public void WriteBoolean(bool value)
    {
        BaseStream.WriteByte((byte)(value ? 0x01 : 0x00));
    }

    public async Task WriteBooleanAsync(bool value)
    {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing Boolean ({value})");
#endif

        await WriteByteAsync((sbyte)(value ? 0x01 : 0x00));
    }

    [WriteMethod]
    public void WriteUnsignedShort(ushort value)
    {
        Span<byte> span = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(span, value);
        BaseStream.Write(span);
    }

    public async Task WriteUnsignedShortAsync(ushort value)
    {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing unsigned Short ({value})");
#endif

        using var write = new RentedArray<byte>(sizeof(ushort));
        BinaryPrimitives.WriteUInt16BigEndian(write, value);
        await WriteAsync(write);
    }

    [WriteMethod]
    public void WriteShort(short value)
    {
        Span<byte> span = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(span, value);
        BaseStream.Write(span);
    }

    public async Task WriteShortAsync(short value)
    {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing Short ({value})");
#endif

        using var write = new RentedArray<byte>(sizeof(short));
        BinaryPrimitives.WriteInt16BigEndian(write, value);
        await WriteAsync(write);
    }

    [WriteMethod]
    public void WriteInt(int value)
    {
        Span<byte> span = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(span, value);
        BaseStream.Write(span);
    }

    public void WriteInt(Enum value) => this.WriteInt(value.GetHashCode());

    public async Task WriteIntAsync(int value)
    {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing Int ({value})");
#endif

        using var write = new RentedArray<byte>(sizeof(int));
        BinaryPrimitives.WriteInt32BigEndian(write, value);
        await WriteAsync(write);
    }

    [WriteMethod]
    public void WriteLong(long value)
    {
        Span<byte> span = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(span, value);
        BaseStream.Write(span);
    }

    public async Task WriteLongAsync(long value)
    {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing Long ({value})");
#endif

        using var write = new RentedArray<byte>(sizeof(long));
        BinaryPrimitives.WriteInt64BigEndian(write, value);
        await WriteAsync(write);
    }

    [WriteMethod]
    public void WriteFloat(float value)
    {
        Span<byte> span = stackalloc byte[4];
        BinaryPrimitives.WriteSingleBigEndian(span, value);
        BaseStream.Write(span);
    }

    public async Task WriteFloatAsync(float value)
    {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing Float ({value})");
#endif

        using var write = new RentedArray<byte>(sizeof(float));
        BinaryPrimitives.WriteSingleBigEndian(write, value);
        await WriteAsync(write);
    }

    [WriteMethod]
    public void WriteDouble(double value)
    {
        Span<byte> span = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(span, value);
        BaseStream.Write(span);
    }

    public async Task WriteDoubleAsync(double value)
    {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing Double ({value})");
#endif

        using var write = new RentedArray<byte>(sizeof(double));
        BinaryPrimitives.WriteDoubleBigEndian(write, value);
        await WriteAsync(write);
    }

    [WriteMethod]
    public void WriteString(string value, int maxLength = short.MaxValue)
    {
        System.Diagnostics.Debug.Assert(value.Length <= maxLength);

        using var bytes = new RentedArray<byte>(Encoding.UTF8.GetByteCount(value));
        Encoding.UTF8.GetBytes(value, bytes.Span);
        WriteVarInt(bytes.Length);
        Write(bytes);
    }

    [WriteMethod]
    public void WriteNullableString(string? value, int maxLength = short.MaxValue)
    {
        if (value is null)
            return;

        System.Diagnostics.Debug.Assert(value.Length <= maxLength);

        using var bytes = new RentedArray<byte>(Encoding.UTF8.GetByteCount(value));
        Encoding.UTF8.GetBytes(value, bytes.Span);
        WriteVarInt(bytes.Length);
        Write(bytes);
    }

    public async Task WriteStringAsync(string value, int maxLength = short.MaxValue)
    {
        //await Globals.PacketLogger.LogDebugAsync($"Writing String ({value})");

        ArgumentNullException.ThrowIfNull(value);

        if (value.Length > maxLength)
            throw new ArgumentException($"string ({value.Length}) exceeded maximum length ({maxLength})", nameof(value));

        using var bytes = new RentedArray<byte>(Encoding.UTF8.GetByteCount(value));
        Encoding.UTF8.GetBytes(value, bytes.Span);
        await WriteVarIntAsync(bytes.Length);
        await WriteAsync(bytes);
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

            await WriteUnsignedByteAsync(temp);
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
    public async Task WriteVarIntAsync(Enum value) => await WriteVarIntAsync(Convert.ToInt32(value));

    public void WriteLongArray(long[] values)
    {
        Span<byte> buffer = stackalloc byte[8];
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteInt64BigEndian(buffer, values[i]);
            BaseStream.Write(buffer);
        }
    }

    public async Task WriteLongArrayAsync(long[] values)
    {
        foreach (var value in values)
            await WriteLongAsync(value);
    }

    public async Task WriteLongArrayAsync(ulong[] values)
    {
        foreach (var value in values)
            await WriteLongAsync((long)value);
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


            await WriteUnsignedByteAsync(temp);
        }
        while (unsigned != 0);
    }

    [WriteMethod]
    //Just for types that aren't impl yet
    public void WriteEmptyObject(object obj)
    {
    }

    public void WriteSoundEvent(SoundEvent soundEvent)
    {
        this.WriteString(soundEvent.ResourceLocation);
        this.WriteOptional(soundEvent.FixedRange);
    }

    [WriteMethod]
    public void WriteSoundEffect(SoundEffect sound)
    {
        this.WriteString(JsonNamingPolicy.SnakeCaseLower.ConvertName(sound.SoundName ?? sound.SoundId.ToString()));

        if (sound.FixedRange.HasValue)
            this.WriteFloat(sound.FixedRange.Value);
    }

    [WriteMethod]
    public void WriteNbtCompound(NbtCompound compound)
    {
        var writer = new NbtWriter(this, true);

        foreach (var (_, tag) in compound)
            writer.WriteTag(tag);

        writer.TryFinish();
    }

    [WriteMethod]
    public void WriteDateTimeOffset(DateTimeOffset date)
    {
        this.WriteLong(date.ToUnixTimeMilliseconds());
    }

    [WriteMethod]
    public void WriteWindowProperty(IWindowProperty windowProperty)
    {
        this.WriteShort(windowProperty.Property);
        this.WriteShort(windowProperty.Value);
    }

    [WriteMethod]
    public void WriteAngle(Angle angle)
    {
        BaseStream.WriteByte(angle.Value);
    }

    [WriteMethod, DataFormat(typeof(float))]
    public void WriteFloatAngle(Angle angle)
    {
        WriteFloat(angle.Degrees);
    }

    public async Task WriteAngleAsync(Angle angle)
    {
        await WriteByteAsync((sbyte)angle.Value);
    }

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

        var writer = new NbtWriter(this, true);

        writer.WriteChatMessage(chatMessage);

        writer.EndCompound();
        writer.TryFinish();
    }

    [WriteMethod]
    public void WriteEquipment(Equipment equipment)
    {
        this.WriteByte((sbyte)equipment.Slot);
        this.WriteItemStack(equipment.Item);
    }

    [WriteMethod]
    public void WriteByteArray(byte[] values)
    {
        BaseStream.Write(values);
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

        this.WriteFloat(value!.Value);
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
        WriteFloat(value.X);
        WriteFloat(value.Y);
        WriteFloat(value.Z);
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
        WriteFloat(value.X);
        WriteFloat(value.Y);
        WriteFloat(value.Z);
    }

    [WriteMethod]
    public void WriteBossBarAction(BossBarAction value) => value.WriteTo(this);

    private void WriteTag(Tag value)
    {
        WriteString(value.Name);
        WriteVarInt(value.Count);
        for (int i = 0; i < value.Entries.Length; i++)
        {
            WriteVarInt(value.Entries[i]);
        }
    }

    [WriteMethod]
    public void WriteTags(IDictionary<string, Tag[]> tagsDictionary)
    {
        this.WriteVarInt(tagsDictionary.Count - 1);

        foreach (var (name, tags) in tagsDictionary)
        {
            if (name == "worldgen")
                continue;

            var namespaceId = $"minecraft:{name.TrimEnd('s')}";
            this.WriteString(namespaceId);

            this.WriteVarInt(tags.Length);
            foreach (var tag in tags)
                this.WriteTag(tag);
        }
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

            this.WriteFloat(advancement.Display.XCoord);
            this.WriteFloat(advancement.Display.YCoord);
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

    public async Task WriteSkinPropertyAsync(SkinProperty skinProperty)
    {
        await this.WriteStringAsync(skinProperty.Name);
        await this.WriteStringAsync(skinProperty.Value);

        var signed = !string.IsNullOrWhiteSpace(skinProperty.Signature);

        await this.WriteBooleanAsync(signed);
        if (signed)
            await this.WriteStringAsync(skinProperty.Signature);
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
    public void WriteCommandNode(CommandNode value)
    {
        value.CopyTo(this);
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
    public void WriteVelocity(Velocity value)
    {
        WriteShort(value.X);
        WriteShort(value.Y);
        WriteShort(value.Z);
    }

    [WriteMethod]
    public void WriteCodec(ICodec codec)
    {
        var writer = new NbtWriter(this, true);

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
    }

    [WriteMethod]
    public void WriteSoundPosition(SoundPosition value)
    {
        WriteInt(value.X);
        WriteInt(value.Y);
        WriteInt(value.Z);
    }

    [WriteMethod]
    public void WritePlayerInfoAction(InfoAction value)
    {
        value.Write(this);
    }

    [WriteMethod]
    public void WriteStatistic(Statistic value)
    {
        WriteVarInt(value.CategoryId);
        WriteVarInt(value.StatisticId);
        WriteVarInt(value.Value);
    }

    public async Task WriteUuidAsync(Guid value)
    {
        //var arr = value.ToByteArray();
        var uuid = System.Numerics.BigInteger.Parse(value.ToString().Replace("-", ""), System.Globalization.NumberStyles.HexNumber);
        await WriteAsync(uuid.ToByteArray(false, true));
    }

    public async Task WriteChatAsync(ChatMessage value) => await WriteStringAsync(value.ToString(Globals.JsonOptions));

    public async Task WritePositionAsync(Vector value)
    {
        var val = (long)(value.X & 0x3FFFFFF) << 38;
        val |= (long)(value.Z & 0x3FFFFFF) << 12;
        val |= (long)(value.Y & 0xFFF);

        await WriteLongAsync(val);
    }

    public async Task WritePositionFAsync(VectorF value)
    {
        var val = (long)((int)value.X & 0x3FFFFFF) << 38;
        val |= (long)((int)value.Z & 0x3FFFFFF) << 12;
        val |= (long)((int)value.Y & 0xFFF);

        await WriteLongAsync(val);
    }

    [WriteMethod]
    public void WriteChunkBiomes(ChunkBiome chunkBiome)
    {
        this.WriteInt(chunkBiome.X);
        this.WriteInt(chunkBiome.Z);

        this.WriteVarInt(chunkBiome.Data.Length);
        this.WriteByteArray(chunkBiome.Data);
    }

    [WriteMethod]
    public void WriteNbt(INbtTag nbt)
    {
        using var writer = new NbtWriter(BaseStream);
        writer.WriteTag(nbt);
    }

    [WriteMethod]
    public void WriteExplosionRecord(ExplosionRecord record)
    {
        WriteByte(record.X);
        WriteByte(record.Y);
        WriteByte(record.Z);
    }
}
