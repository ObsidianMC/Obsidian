using Obsidian.API.Advancements;
using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.API.Registry.Codecs.ArmorTrims.TrimMaterial;
using Obsidian.API.Registry.Codecs.ArmorTrims.TrimPattern;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.Registry.Codecs.Chat;
using Obsidian.API.Registry.Codecs.DamageTypes;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.API.Registry.Codecs.PaintingVariant;
using Obsidian.API.Registry.Codecs.WolfVariant;
using Obsidian.API.Utilities;
using Obsidian.Commands;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Actions.BossBar;
using Obsidian.Net.Actions.PlayerInfo;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.WindowProperties;
using Obsidian.Registries;
using Obsidian.Serialization.Attributes;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Obsidian.Net;

public partial class MinecraftStream : INetStreamWriter
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

        await WriteUnsignedByteAsync((byte)value);
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

    [WriteMethod]
    public void WriteSoundEffect(SoundEffect sound)
    {
        this.WriteString(JsonNamingPolicy.SnakeCaseLower.ConvertName(sound.SoundName ?? sound.SoundId.ToString()));

        if (sound.HasFixedRange)
            this.WriteFloat(sound.Range);
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

        this.WriteChatNbt(writer, chatMessage);

        writer.EndCompound();
        writer.TryFinish();
    }

    private void WriteChatNbt(NbtWriter writer, ChatMessage chatMessage)
    {
        if (!chatMessage.Text.IsNullOrEmpty())
            writer.WriteString("text", chatMessage.Text);
        if (!chatMessage.Translate.IsNullOrEmpty())
            writer.WriteString("translate", chatMessage.Translate);
        if (chatMessage.Color.HasValue)
            writer.WriteString("color", chatMessage.Color.Value.ToString());
        if (!chatMessage.Insertion.IsNullOrEmpty())
            writer.WriteString("insertion", chatMessage.Insertion);

        writer.WriteBool("bold", chatMessage.Bold);
        writer.WriteBool("italic", chatMessage.Italic);
        writer.WriteBool("underlined", chatMessage.Underlined);
        writer.WriteBool("strikethrough", chatMessage.Strikethrough);
        writer.WriteBool("obfuscated", chatMessage.Obfuscated);

        if (chatMessage.ClickEvent != null)
            writer.WriteTag(chatMessage.ClickEvent.ToNbt());
        if (chatMessage.HoverEvent != null)
            writer.WriteTag(chatMessage.HoverEvent.ToNbt());

        if (chatMessage.Extra is List<ChatMessage> extras)
        {
            var list = new NbtList(NbtTagType.Compound, "extra");

            foreach (var item in extras)
                list.Add(item.ToNbt());

            writer.WriteTag(list);
        }

        if (chatMessage.With is List<ChatMessage> extraChatComponents)
        {
            var list = new NbtList(NbtTagType.Compound, "with");

            foreach (var item in extraChatComponents)
                list.Add(item.ToNbt());

            writer.WriteTag(list);
        }
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
    public void WriteItemStack(ItemStack value)
    {
        value ??= new ItemStack(0, 0) { Present = true };

        var item = value.AsItem();
        WriteVarInt(item.Id);

        if (item.Id != 0)
        {
            WriteByte((sbyte)value.Count);

            NbtWriter writer = new(this, true);

            ItemMeta meta = value.ItemMeta;

            if (meta.HasTags())
            {
                writer.WriteByte("Unbreakable", (byte)(meta.Unbreakable ? 1 : 0));

                if (meta.Durability > 0)
                    writer.WriteInt("Damage", meta.Durability);

                if (meta.CustomModelData > 0)
                    writer.WriteInt("CustomModelData", meta.CustomModelData);

                if (meta.CanDestroy is not null)
                {
                    writer.WriteListStart("CanDestroy", NbtTagType.String, meta.CanDestroy.Count);

                    foreach (var block in meta.CanDestroy)
                        writer.WriteString(block);

                    writer.EndList();
                }

                if (meta.Name is not null)
                {
                    writer.WriteCompoundStart("display");

                    writer.WriteString("Name", new List<ChatMessage> { meta.Name }.ToJson());

                    if (meta.Lore is not null)
                    {
                        writer.WriteListStart("Lore", NbtTagType.String, meta.Lore.Count);

                        foreach (var lore in meta.Lore)
                            writer.WriteString(new List<ChatMessage> { lore }.ToJson());

                        writer.EndList();
                    }

                    writer.EndCompound();
                }
                else if (meta.Lore is not null)
                {
                    writer.WriteCompoundStart("display");

                    writer.WriteListStart("Lore", NbtTagType.String, meta.Lore.Count);

                    foreach (var lore in meta.Lore)
                        writer.WriteString(new List<ChatMessage> { lore }.ToJson());

                    writer.EndList();

                    writer.EndCompound();
                }
            }
            writer.WriteString("id", item.UnlocalizedName);
            writer.WriteByte("Count", (byte)value.Count);

            writer.EndCompound();
            writer.TryFinish();
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

    public async Task WriteEntityMetdata(byte index, EntityMetadataType type, object value, bool optional = false)
    {
        await WriteUnsignedByteAsync(index);
        await WriteVarIntAsync((int)type);
        switch (type)
        {
            case EntityMetadataType.Byte:
                await WriteUnsignedByteAsync((byte)value);
                break;

            case EntityMetadataType.VarInt:
                await WriteVarIntAsync((int)value);
                break;

            case EntityMetadataType.Float:
                await WriteFloatAsync((float)value);
                break;

            case EntityMetadataType.String:
                await WriteStringAsync((string)value);
                break;

            case EntityMetadataType.Chat:
                await WriteChatAsync((ChatMessage)value);
                break;

            case EntityMetadataType.OptChat:
                await WriteBooleanAsync(optional);

                if (optional)
                    await WriteChatAsync((ChatMessage)value);
                break;

            case EntityMetadataType.Slot:
                await WriteSlotAsync((ItemStack)value);
                break;

            case EntityMetadataType.Boolean:
                await WriteBooleanAsync((bool)value);
                break;

            case EntityMetadataType.Rotation:
                break;

            case EntityMetadataType.Position:
                await WritePositionFAsync((VectorF)value);
                break;

            case EntityMetadataType.OptPosition:
                await WriteBooleanAsync(optional);

                if (optional)
                    await WritePositionFAsync((VectorF)value);

                break;

            case EntityMetadataType.Direction:
                break;

            case EntityMetadataType.OptUuid:
                await WriteBooleanAsync(optional);

                if (optional)
                    await WriteUuidAsync((Guid)value);
                break;

            case EntityMetadataType.OptBlockId:
                await WriteVarIntAsync((int)value);
                break;

            case EntityMetadataType.Nbt:
            case EntityMetadataType.Particle:
            case EntityMetadataType.VillagerData:
            case EntityMetadataType.OptVarInt:
                if (optional)
                {
                    await WriteVarIntAsync(0);
                    break;
                }
                await WriteVarIntAsync(1 + (int)value);
                break;
            case EntityMetadataType.Pose:
                await WriteVarIntAsync((Pose)value);
                break;
            default:
                break;
        }
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

    public async Task WriteSlotAsync(ItemStack slot)
    {
        if (slot is null)
            slot = new ItemStack(0, 0)
            {
                Present = true
            };

        var item = slot.AsItem();

        await WriteBooleanAsync(slot.Present);
        if (slot.Present)
        {
            await WriteVarIntAsync(item.Id);
            await WriteByteAsync((sbyte)slot.Count);

            var writer = new NbtWriter(this, true);

            var itemMeta = slot.ItemMeta;

            //TODO write enchants
            if (itemMeta.HasTags())
            {
                writer.WriteBool("Unbreakable", itemMeta.Unbreakable);

                if (itemMeta.Durability > 0)
                    writer.WriteInt("Damage", itemMeta.Durability);

                if (itemMeta.CustomModelData > 0)
                    writer.WriteInt("CustomModelData", itemMeta.CustomModelData);

                if (itemMeta.CanDestroy != null)
                {
                    writer.WriteListStart("CanDestroy", NbtTagType.String, itemMeta.CanDestroy.Count);

                    foreach (var block in itemMeta.CanDestroy)
                        writer.WriteString(block);

                    writer.EndList();
                }

                if (itemMeta.Name != null)
                {
                    writer.WriteCompoundStart("display");

                    writer.WriteString("Name", new List<ChatMessage> { itemMeta.Name }.ToJson());

                    if (itemMeta.Lore != null)
                    {
                        writer.WriteListStart("Lore", NbtTagType.String, itemMeta.Lore.Count);

                        foreach (var lore in itemMeta.Lore)
                            writer.WriteString(new List<ChatMessage> { lore }.ToJson());

                        writer.EndList();
                    }

                    writer.EndCompound();
                }
                else if (itemMeta.Lore != null)
                {
                    writer.WriteCompoundStart("display");

                    writer.WriteListStart("Lore", NbtTagType.String, itemMeta.Lore.Count);

                    foreach (var lore in itemMeta.Lore)
                        writer.WriteString(new List<ChatMessage> { lore }.ToJson());

                    writer.EndList();

                    writer.EndCompound();
                }
            }

            writer.WriteString("id", item.UnlocalizedName);
            writer.WriteByte("Count", (byte)slot.Count);

            writer.EndCompound();
            await writer.TryFinishAsync();
        }
    }

    internal async Task WriteRecipeAsync(string name, IRecipe recipe)
    {
        await WriteStringAsync($"minecraft:{recipe.Type.ToString().ToSnakeCase()}");

        await WriteStringAsync(name);

        if (recipe is ShapedRecipe shapedRecipe)
        {
            var patterns = shapedRecipe.Pattern;

            int width = patterns[0].Length, height = patterns.Count;

            await WriteStringAsync(shapedRecipe.Group ?? "");
            await WriteVarIntAsync(shapedRecipe.Category);

            await WriteVarIntAsync(width);
            await WriteVarIntAsync(height);

            var ingredients = new List<ItemStack>[width * height];

            var y = 0;
            foreach (var pattern in patterns)
            {
                var x = 0;
                foreach (var c in pattern)
                {
                    var preX = ++x;

                    if (char.IsWhiteSpace(c))
                        continue;

                    var index = preX + (y * width);

                    var key = shapedRecipe.Key[c];

                    foreach (var item in key)
                    {
                        if (ingredients[index] is null)
                            ingredients[index] = new List<ItemStack> { item };
                        else
                            ingredients[index].Add(item);
                    }
                }
                y++;
            }

            foreach (var items in ingredients)
            {
                if (items == null)
                {
                    await WriteVarIntAsync(0);
                    continue;
                }

                await WriteVarIntAsync(items.Count);

                foreach (var itemStack in items)
                    await WriteSlotAsync(itemStack);
            }

            await WriteSlotAsync(shapedRecipe.Result.First());
        }
        else if (recipe is ShapelessRecipe shapelessRecipe)
        {
            var ingredients = shapelessRecipe.Ingredients;

            await WriteStringAsync(shapelessRecipe.Group ?? "");
            await WriteVarIntAsync(shapelessRecipe.Category);

            await WriteVarIntAsync(ingredients.Count);
            foreach (var ingredient in ingredients)
            {
                await WriteVarIntAsync(ingredient.Count);
                foreach (var item in ingredient)
                    await WriteSlotAsync(item);
            }

            var result = shapelessRecipe.Result.First();

            await WriteSlotAsync(result);
        }
        else if (recipe is SmeltingRecipe smeltingRecipe)
        {
            await WriteStringAsync(smeltingRecipe.Group ?? "");
            await WriteVarIntAsync(smeltingRecipe.Category);

            await WriteVarIntAsync(smeltingRecipe.Ingredient.Count);
            foreach (var i in smeltingRecipe.Ingredient)
                await WriteSlotAsync(i);

            await WriteSlotAsync(smeltingRecipe.Result.First());

            await WriteFloatAsync(smeltingRecipe.Experience);
            await WriteVarIntAsync(smeltingRecipe.CookingTime);
        }
        else if (recipe is CuttingRecipe cuttingRecipe)
        {
            await WriteStringAsync(cuttingRecipe.Group ?? "");

            await WriteVarIntAsync(cuttingRecipe.Ingredient.Count);
            foreach (var item in cuttingRecipe.Ingredient)
                await WriteSlotAsync(item);


            var result = cuttingRecipe.Result.First();

            result.Count = (short)cuttingRecipe.Count;

            await WriteSlotAsync(result);
        }
        else if (recipe is SmithingTransformRecipe smithingTransformRecipe)
        {
            await WriteVarIntAsync(smithingTransformRecipe.Template.Count);
            foreach (var item in smithingTransformRecipe.Template)
                await WriteSlotAsync(item);

            await WriteVarIntAsync(smithingTransformRecipe.Base.Count);
            foreach (var item in smithingTransformRecipe.Base)
                await WriteSlotAsync(item);

            await WriteVarIntAsync(smithingTransformRecipe.Addition.Count);
            foreach (var item in smithingTransformRecipe.Addition)
                await WriteSlotAsync(item);

            await WriteSlotAsync(smithingTransformRecipe.Result.First());
        }
        else if (recipe is SmithingTrimRecipe smithingTrimRecipe)
        {
            await WriteVarIntAsync(smithingTrimRecipe.Template.Count);
            foreach (var item in smithingTrimRecipe.Template)
                await WriteSlotAsync(item);

            await WriteVarIntAsync(smithingTrimRecipe.Base.Count);
            foreach (var item in smithingTrimRecipe.Base)
                await WriteSlotAsync(item);

            await WriteVarIntAsync(smithingTrimRecipe.Addition.Count);
            foreach (var item in smithingTrimRecipe.Addition)
                await WriteSlotAsync(item);
        }
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
    public void WriteRecipes(IDictionary<string, IRecipe> recipes)
    {
        WriteVarInt(recipes.Count);
        foreach (var (name, recipe) in recipes)
            WriteRecipe(name, recipe);
    }

    public void WriteRecipe(string name, IRecipe recipe)
    {
        WriteString($"minecraft:{recipe.Type.ToString().ToSnakeCase()}");

        WriteString(name);

        if (recipe is ShapedRecipe shapedRecipe)
        {
            var patterns = shapedRecipe.Pattern;

            int width = patterns[0].Length, height = patterns.Count;

            WriteVarInt(width);
            WriteVarInt(height);

            WriteString(shapedRecipe.Group ?? string.Empty);
            WriteVarInt(shapedRecipe.Category);

            var ingredients = new List<ItemStack>[width * height];

            var y = 0;
            foreach (var pattern in patterns)
            {
                var x = 0;
                foreach (var c in pattern)
                {
                    if (char.IsWhiteSpace(c))
                    {
                        x++;
                        continue;
                    }

                    var index = x + (y * width);
                    var key = shapedRecipe.Key[c];

                    foreach (var item in key)
                    {
                        if (ingredients[index] is null)
                            ingredients[index] = new List<ItemStack> { item };
                        else
                            ingredients[index].Add(item);
                    }

                    x++;
                }
                y++;
            }

            foreach (var items in ingredients)
            {
                if (items == null)
                {
                    WriteVarInt(0);
                    continue;
                }

                WriteVarInt(items.Count);

                foreach (var itemStack in items)
                    WriteItemStack(itemStack);
            }

            WriteItemStack(shapedRecipe.Result.First());
        }
        else if (recipe is ShapelessRecipe shapelessRecipe)
        {
            var ingredients = shapelessRecipe.Ingredients;

            WriteString(shapelessRecipe.Group ?? string.Empty);
            WriteVarInt(shapelessRecipe.Category);

            WriteVarInt(ingredients.Count);
            foreach (var ingredient in ingredients)
            {
                WriteVarInt(ingredient.Count);
                foreach (var item in ingredient)
                    WriteItemStack(item);
            }

            var result = shapelessRecipe.Result.First();

            WriteItemStack(result);
        }
        else if (recipe is SmeltingRecipe smeltingRecipe)
        {
            WriteString(smeltingRecipe.Group ?? string.Empty);
            WriteVarInt(smeltingRecipe.Category);

            WriteVarInt(smeltingRecipe.Ingredient.Count);
            foreach (var i in smeltingRecipe.Ingredient)
                WriteItemStack(i);

            WriteItemStack(smeltingRecipe.Result.First());

            WriteFloat(smeltingRecipe.Experience);
            WriteVarInt(smeltingRecipe.CookingTime);
        }
        else if (recipe is CuttingRecipe cuttingRecipe)
        {
            WriteString(cuttingRecipe.Group ?? string.Empty);

            WriteVarInt(cuttingRecipe.Ingredient.Count);
            foreach (var item in cuttingRecipe.Ingredient)
                WriteItemStack(item);

            var result = cuttingRecipe.Result.First();

            result.Count = (short)cuttingRecipe.Count;

            WriteItemStack(result);
        }
        else if (recipe is SmithingTransformRecipe smithingTransformRecipe)
        {
            WriteVarInt(smithingTransformRecipe.Template.Count);
            foreach (var item in smithingTransformRecipe.Template)
                WriteItemStack(item);

            WriteVarInt(smithingTransformRecipe.Base.Count);
            foreach (var item in smithingTransformRecipe.Base)
                WriteItemStack(item);

            WriteVarInt(smithingTransformRecipe.Addition.Count);
            foreach (var item in smithingTransformRecipe.Addition)
                WriteItemStack(item);

            WriteItemStack(smithingTransformRecipe.Result.First());
        }
        else if (recipe is SmithingTrimRecipe smithingTrimRecipe)
        {
            WriteVarInt(smithingTrimRecipe.Template.Count);
            foreach (var item in smithingTrimRecipe.Template)
                WriteItemStack(item);

            WriteVarInt(smithingTrimRecipe.Base.Count);
            foreach (var item in smithingTrimRecipe.Base)
                WriteItemStack(item);

            WriteVarInt(smithingTrimRecipe.Addition.Count);
            foreach (var item in smithingTrimRecipe.Addition)
                WriteItemStack(item);
        }
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


    [WriteMethod]
    public void WriteParticleData(ParticleData value)
    {
        if (value is null || value == ParticleData.None)
            return;

        switch (value.ParticleType)
        {
            case ParticleType.Block:
                WriteVarInt(value.GetDataAs<int>());
                break;

            case ParticleType.Dust:
                var (red, green, blue, scale) = value.GetDataAs<(float, float, float, float)>();
                WriteFloat(red);
                WriteFloat(green);
                WriteFloat(blue);
                WriteFloat(scale);
                break;

            case ParticleType.FallingDust:
                WriteVarInt(value.GetDataAs<int>());
                break;

            case ParticleType.Item:
                WriteItemStack(value.GetDataAs<ItemStack>());
                break;
        }
    }
}
