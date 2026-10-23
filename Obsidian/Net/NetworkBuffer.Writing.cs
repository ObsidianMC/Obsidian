using Obsidian.API.Advancements;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
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
    /// <summary>
    /// Writes a UUID as vanilla does: its most significant 64 bits, then its least significant, as two longs (16
    /// bytes, the reverse of <see cref="ReadGuid"/>).
    /// </summary>
    public void WriteUuid(Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        value.TryWriteBytes(bytes, bigEndian: true, out _);
        Write(bytes);
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
    public void WriteByteArray(ReadOnlySpan<byte> values) => this.Write(values);

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

    // Vanilla 1.21.11 sends requirements and telemetry here; criteria definitions remain on the server.
    public void WriteAdvancement(Advancement advancement)
    {
        var hasParent = !string.IsNullOrEmpty(advancement.Parent);
        this.WriteBoolean(hasParent);

        if (hasParent)
            this.WriteString(advancement.Parent);

        var hasDisplay = advancement.Display is not null;

        this.WriteBoolean(hasDisplay);

        if (hasDisplay)
        {
            this.WriteChat(advancement.Display.Title);
            this.WriteChat(advancement.Display.Description);

            this.WriteItemStack(advancement.Display.IconStack ?? new ItemStack(advancement.Display.Icon, 1));

            this.WriteVarInt(advancement.Display.AdvancementFrameType);

            this.WriteInt((int)advancement.Display.Flags);

            if (advancement.Display.Flags.HasFlag(AdvancementFlags.HasBackgroundTexture))
                this.WriteString(advancement.Display.BackgroundTexture);

            this.WriteSingle(advancement.Display.XCoord);
            this.WriteSingle(advancement.Display.YCoord);
        }

        var requirements = advancement.Requirements.Count > 0
            ? advancement.Requirements
            : advancement.Criteria.Where(criterion => criterion.Required)
                .Select(criterion => new List<string> { criterion.Identifier }).ToList();
        this.WriteVarInt(requirements.Count);
        foreach (var row in requirements)
        {
            this.WriteVarInt(row.Count);
            foreach (var criterion in row)
                this.WriteString(criterion);
        }

        this.WriteBoolean(advancement.SendsTelemetryEvent);
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
        if (chatMessage is null)
            return;

        if (chatMessage.Translate is null && chatMessage.Keybind is null && chatMessage.Color is null && chatMessage.Bold is null && chatMessage.Italic is null
            && chatMessage.Underlined is null && chatMessage.Strikethrough is null && chatMessage.Obfuscated is null
            && chatMessage.ShadowColor is null && chatMessage.Insertion is null && chatMessage.ClickEvent is null && chatMessage.HoverEvent is null
            && !chatMessage.GetExtras().Any() && !chatMessage.GetExtraChatComponents().Any())
        {
            ModifiedUtf8.TryGetBytes(chatMessage.Text ?? "", out var text);
            this.WriteByte((byte)8);
            this.WriteUnsignedShort(checked((ushort)text!.Length));
            this.Write(text);
            return;
        }

        using var writer = new RawNbtWriter(true);

        writer.WriteChatMessage(chatMessage);

        writer.EndCompound();
        writer.TryFinish();

        this.Write(writer.Data);
    }

    /// <summary>Writes a network NBT compound, or an empty (end) tag for null.</summary>
    public void WriteOptionalNbtCompound(NbtCompound? compound)
    {
        if (compound is null)
            this.WriteByte((byte)NbtTagType.End);
        else
            this.WriteNbtCompound(compound);
    }

    /// <summary>
    /// Writes a bit set of <paramref name="size"/> bits as <c>ceil(size / 8)</c> bytes, lowest bit first (Java's
    /// <c>BitSet.toByteArray</c>, padded to the size).
    /// </summary>
    public void WriteFixedBitSet(BitSet bits, int size)
    {
        var bytes = new byte[(size + 7) / 8];
        for (var i = 0; i < size; i++)
        {
            if (bits.GetBit(i))
                bytes[i / 8] |= (byte)(1 << (i % 8));
        }
        this.WriteByteArray(bytes);
    }

    public void WriteNbtCompound(NbtCompound compound)
    {
        using var writer = new RawNbtWriter(true);

        foreach (var (_, tag) in compound)
            writer.WriteTag(tag);

        writer.EndCompound();
        writer.TryFinish();

        this.Write(writer.Data);
    }

    [WriteMethod]
    public void WriteCodec(ICodec codec)
    {
        using var writer = new RawNbtWriter(true);

        codec.WriteElement(writer);

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
    public void WriteItemStack(ItemStack? value) => this.WriteItemStack(value, false);

    /// <summary>Writes the creative-slot codec when delimitedComponents is true.</summary>
    public void WriteItemStack(ItemStack? value, bool delimitedComponents)
    {
        if (this.IdMaps is null)
        {
            this.WriteItemStackPatch(value, delimitedComponents);
            return;
        }

        // A stack whose components name an entry the connection lacks is written as nothing, like a missing item, and
        // so is one whose components hold such a required stack (a bundle's items, a use remainder), which can't be.
        var encoded = new NetworkBuffer { IdMaps = this.IdMaps };
        try
        {
            encoded.WriteItemStackPatch(value, delimitedComponents);
        }
        catch (MissingRegistryEntryException)
        {
            this.WriteVarInt(0);
            return;
        }

        this.Write(encoded.data.AsSpan(0, encoded.Offset));
    }

    private void WriteItemStackPatch(ItemStack? value, bool delimitedComponents)
    {
        value ??= ItemStack.Air;

        var item = value.AsItem();

        // An item the connection lacks, like one of the client's mods' the server doesn't run, is written as nothing.
        var itemId = this.RemoteItemId(item.Id);
        if (value.Count <= 0 || value.IsAir || itemId < 0)
        {
            this.WriteVarInt(0);
            return;
        }
        WriteVarInt(value.Count);

        WriteVarInt(itemId);

        // Like vanilla's DataComponentPatch: only the components set on the stack. The client knows the item's defaults,
        // and Obsidian's placeholders for them (e.g. a max stack size of 64) would override them.
        // Encode before counting: a plugin may start unloading after projection. No codec delegate escapes its lease,
        // and values refused at encoding time are omitted from both the count and payload.
        var components = new List<(DataComponentType Type, byte[] Bytes)>();
        foreach (var component in SentPatch(value))
        {
            using var encoded = new NetworkBuffer { IdMaps = this.IdMaps };
            if (component is PluginDataComponent plugin)
            {
                if (!plugin.TryWriteNetwork(encoded))
                    continue;
            }
            else
                encoded.WriteDataComponent(component);

            components.Add((component.Type, encoded.data.AsSpan(0, encoded.Offset).ToArray()));
        }

        WriteVarInt(components.Count);
        WriteVarInt(value.RemoveComponents.Count);

        foreach (var component in components)
        {
            this.WriteComponentType(component.Type);

            if (delimitedComponents)
            {
                this.WriteVarInt(component.Bytes.Length);
                this.Write(component.Bytes);
            }
            else
                this.Write(component.Bytes);
        }

        foreach (var componentType in value.RemoveComponents)
            this.WriteComponentType(componentType);
    }

    // An item's and a data component type's id, in the connection's numbering (see IdMaps).
    private int RemoteItemId(int id) => this.IdMaps is RegistryIdMaps maps ? maps.Item.ToRemote(id) : id;

    // A component type the connection lacks throws, like any registry entry (see ToRemoteId).
    private void WriteComponentType(DataComponentType type) =>
        this.WriteVarInt(this.ToRemoteId(RegistryIdMaps.DataComponentTypes, (int)type));

    // Whether the connection has the component type, which an item stack can only be written with if it does.
    private bool HasComponentType(DataComponentType type) =>
        this.IdMaps is not RegistryIdMaps maps || maps.DataComponentType.ToRemote((int)type) >= 0;

    public void WriteRegistryId(string registry, int id) => this.WriteVarInt(this.ToRemoteId(registry, id));

    public void WriteRegistryHolder(string registry, int? id) =>
        this.WriteVarInt(id is int value ? this.ToRemoteId(registry, value) + 1 : 0);

    // The connection's id of this process's id of the registry (see IdMaps). An entry the connection lacks throws, and
    // the enclosing optional stack (WriteItemStack) is then written as nothing.
    private int ToRemoteId(string registry, int id)
    {
        if (this.IdMaps is not RegistryIdMaps maps)
            return id;

        var remote = maps.Get(registry).ToRemote(id);
        return remote >= 0 ? remote : throw new MissingRegistryEntryException(registry, id);
    }

    public void WriteUntrustedItemStack(ItemStack? value) => this.WriteItemStack(value, true);

    /// <summary>
    /// Writes a stack that can't be empty, like vanilla's <c>ItemStack.STREAM_CODEC</c>.
    /// </summary>
    /// <remarks>
    /// Unlike an optional stack, it can't be written as nothing when it names an entry the connection lacks. That
    /// throws instead, so the enclosing optional stack (<see cref="WriteItemStack(ItemStack?)"/>) is written as
    /// nothing.
    /// </remarks>
    public void WriteRequiredItemStack(ItemStack value)
    {
        if (value is null || value.IsAir || value.Count <= 0)
            throw new ArgumentException("Expected a nonempty stack.", nameof(value));

        var item = value.AsItem();
        if (this.RemoteItemId(item.Id) < 0)
            throw new MissingRegistryEntryException(RegistryIdMaps.Items, item.Id);

        this.WriteItemStackPatch(value, false);
    }

    public void WriteItemStackList(params ReadOnlySpan<ItemStack?> values) => this.WriteLengthPrefixedArray(this.WriteItemStack, values);

    public void WriteLengthPrefixedArray<TValue>(Action<TValue> write, params ReadOnlySpan<TValue> values)
    {
        this.WriteVarInt(values.Length);

        foreach (var value in values)
            write(value);
    }

    public void WriteLengthPrefixedArray(bool showInTooltips, params Enchantment[] enchantments)
    {
        this.WriteVarInt(enchantments.Length);

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
            this.WritePacket(delimiterPacket);
            foreach (var p in bp.Packets)
                this.WritePacket(p);
            this.WritePacket(delimiterPacket);
            return;
        }

        using var body = SerializeBody(packet, this.WritesPluginEntityData);
        this.WriteFrame(body.AsSpan(0, body.Size), compressionThreshold: -1);
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

        using var body = SerializeBody(packet, this.WritesPluginEntityData);
        this.WriteFrame(body.AsSpan(0, body.Size), compressionThreshold);
    }

    /// <summary>
    /// Serializes <paramref name="packet"/> as the bodies of the frames it's sent in, in order: one, its id and then its
    /// fields, or for a bundle its packets' between two delimiters. Frame them with <see cref="WriteFrame"/>.
    /// </summary>
    internal static void SerializeBodies(IClientboundPacket packet, bool writesPluginEntityData, List<NetworkBuffer> bodies)
    {
        if (packet is BundledPacket bundle)
        {
            bodies.Add(SerializeBody(delimiterPacket, writesPluginEntityData));
            foreach (var inner in bundle.Packets)
                SerializeBodies(inner, writesPluginEntityData, bodies);
            bodies.Add(SerializeBody(delimiterPacket, writesPluginEntityData));
            return;
        }

        bodies.Add(SerializeBody(packet, writesPluginEntityData));
    }

    // Most bodies (movement, entity data, block changes) fit in this, so they're written without regrowing the buffer;
    // larger ones double from it.
    private const int BodyStartCapacity = 64;

    private static NetworkBuffer SerializeBody(IClientboundPacket packet, bool writesPluginEntityData)
    {
        var body = new NetworkBuffer(BodyStartCapacity) { WritesPluginEntityData = writesPluginEntityData };
        body.WriteVarInt(packet.Id);
        packet.Serialize(body);
        return body;
    }

    // Where WriteFrame compresses a body before it can write the frame's length ahead of it. Reused by every frame the
    // thread writes: it's copied out before WriteFrame returns, and nothing WriteFrame calls frames another packet.
    [ThreadStatic]
    private static MemoryStream? compressionScratch;

    // A scratch grown past this by a rare huge packet (registry data, say) is dropped rather than kept for the life of
    // the thread.
    private const int MaxRetainedCompressionScratch = 1024 * 1024;

    /// <summary>
    /// Writes a packet's frame around its <paramref name="body"/> (its id and fields): length-prefixed and, with
    /// compression on (a <paramref name="compressionThreshold"/> of 0 or more), with its uncompressed length, zlib
    /// compressed from the threshold and 0 below it.
    /// </summary>
    internal void WriteFrame(ReadOnlySpan<byte> body, int compressionThreshold)
    {
        if (compressionThreshold < 0)
        {
            this.WriteVarInt(body.Length);
            this.Write(body);
        }
        else if (body.Length >= compressionThreshold)
        {
            var compressed = compressionScratch ??= new MemoryStream();

            // Also rewinds it: the position never passes the length.
            compressed.SetLength(0);
            using (ZLibStream zlibStream = new(compressed, CompressionLevel.Optimal, true))
            {
                zlibStream.Write(body);
            }

            var compressedLength = (int)compressed.Length;

            this.WriteVarInt(body.Length.GetVarIntLength() + compressedLength);
            this.WriteVarInt(body.Length);
            this.Write(compressed.GetBuffer().AsSpan(0, compressedLength));

            if (compressed.Capacity > MaxRetainedCompressionScratch)
                compressionScratch = null;
        }
        else
        {
            // Below the threshold, an uncompressed length of 0 marks the body as sent as is.
            this.WriteVarInt(body.Length + 1);
            this.WriteVarInt(0);
            this.Write(body);
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
        var maxAbsValue = NumericsHelper.AbsMax(value.X, NumericsHelper.AbsMax(value.Y, value.Z));

        if (double.IsNaN(maxAbsValue) || maxAbsValue < 1.0d / MaxQuantizedValue)
        {
            this.WriteByte(0);
            return;
        }

        var scaleFactor = (long)Math.Ceiling(maxAbsValue);
        var needsExtraBytes = (scaleFactor & ScaleBits) != scaleFactor;
        var packedScale = needsExtraBytes ? (scaleFactor & ScaleBits) | ContinuiationBit : scaleFactor;

        var packedX = Pack(value.X / scaleFactor) << 3;
        var packedY = Pack(value.Y / scaleFactor) << 18;
        var packedZ = Pack(value.Z / scaleFactor) << 33;

        var packedData = packedZ | packedY | packedX | packedScale;
        this.WriteByte((byte)packedData);
        this.WriteByte((byte)(packedData >> 8));
        this.WriteInt((int)(packedData >> 16));

        if (needsExtraBytes)
            this.WriteVarInt((int)(scaleFactor >> 2));
    }

    #region optionals
    private bool ShouldWriteOptional<TValue>(TValue? value)
    {
        var notNull = value is not null;

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

    public byte[] ToArray() => this.data;

    private const double MaxQuantizedValue = short.MaxValue - 1;
    private const double VelocityPackingScale = 0.5;
    private const long ContinuiationBit = 0x04L;
    private const long ScaleBits = 0x03L;

    private static long Pack(double value) =>
      (long)Math.Round((value * VelocityPackingScale + VelocityPackingScale) * MaxQuantizedValue);

    private static double Unpack(long value) =>
        Math.Min((value & short.MaxValue), MaxQuantizedValue) * 2.0 / MaxQuantizedValue - 1.0;
}
