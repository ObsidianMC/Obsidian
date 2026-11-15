using Obsidian.API.Advancements;
using Obsidian.API.Inventory;
using System.ComponentModel;

namespace Obsidian.API;
/// <summary>
/// Writes packet data in Minecraft's protocol formats, advancing <see cref="INetStream.Offset"/>. Numbers are
/// big-endian.
/// </summary>
public interface INetStreamWriter : INetStream
{
    /// <summary>
    /// Gets a value indicating whether the stream can be written to.
    /// </summary>
    public bool CanWrite { get; }
    /// <summary>
    /// Appends the first <see cref="INetStream.Size"/> bytes of <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">The stream to copy from.</param>
    public void Write(INetStream buffer);
    /// <summary>
    /// Writes a signed byte.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteByte(sbyte value);
    /// <summary>
    /// Writes an enum member's value as a byte.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteByte(Enum value);
    /// <summary>
    /// Writes an unsigned byte.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteByte(byte value);
    /// <summary>
    /// Writes a boolean as a byte: 1 for <see langword="true"/>, 0 for <see langword="false"/>.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteBoolean(bool value);
    /// <summary>
    /// Writes a 16-bit unsigned integer.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteUnsignedShort(ushort value);
    /// <summary>
    /// Writes a 16-bit integer.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteShort(short value);

    /// <summary>
    /// Writes a 32-bit integer.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteInt(int value);
    /// <summary>
    /// Writes an enum member's value as a 32-bit integer.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteInt(Enum value);
    /// <summary>
    /// Writes a 64-bit integer.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteLong(long value);

    /// <summary>
    /// Writes a single-precision float.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteSingle(float value);
    /// <summary>
    /// Writes a double-precision float.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteDouble(double value);

    /// <summary>
    /// Writes a UTF-8 string prefixed with its length in bytes as a VarInt.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <param name="maxLength">The most characters the string may have; only checked in debug builds.</param>
    public void WriteString(string value, int maxLength = short.MaxValue);
    /// <summary>
    /// Writes a variable-length 32-bit integer (VarInt).
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteVarInt(int value);
    /// <summary>
    /// Writes an enum member's value as a VarInt.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteVarInt(Enum value);

    /// <summary>
    /// Writes this process's id <paramref name="id"/> of the built-in registry <paramref name="registry"/> (e.g.
    /// <c>minecraft:block</c>), in the connection's numbering where it numbers the registry differently. The default
    /// implementation, for streams that don't translate ids, writes it as it is.
    /// </summary>
    /// <exception cref="InvalidOperationException">The connection lacks the entry.</exception>
    public void WriteRegistryId(string registry, int id) => this.WriteVarInt(id);

    /// <summary>
    /// Writes a holder like vanilla's <c>ByteBufCodecs.holder</c>: the registry id <paramref name="id"/> (see
    /// <see cref="WriteRegistryId"/>), or, if null, the marker of a value the caller writes inline next.
    /// </summary>
    public void WriteRegistryHolder(string registry, int? id) => this.WriteVarInt(id is int value ? value + 1 : 0);

    /// <summary>
    /// Writes 64-bit integers, without a length prefix.
    /// </summary>
    /// <param name="values">The values to write.</param>
    public void WriteLongArray(long[] values);
    /// <summary>
    /// Writes a variable-length 64-bit integer (VarLong).
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteVarLong(long value);

    /// <summary>
    /// Writes the header of an entity metadata entry: its index, then its type as a VarInt. The value follows.
    /// </summary>
    /// <param name="index">The entry's index.</param>
    /// <param name="type">The entry's value type.</param>
    public void WriteEntityMetadataType(byte index, EntityMetadataType type);
    /// <summary>
    /// Writes <paramref name="entity"/>'s metadata entries, then the <c>0xff</c> end marker.
    /// </summary>
    /// <param name="entity">The entity to write.</param>
    public void WriteEntity(IEntity entity);
    /// <summary>
    /// Writes a bit set as a VarInt count of 64-bit integers, then the integers.
    /// </summary>
    /// <param name="bitset">The bit set to write.</param>
    /// <param name="isFixed">Writes only the set's first 8 bits as one byte instead.</param>
    public void WriteBitSet(BitSet bitset, bool isFixed = false);
    /// <summary>
    /// Writes a text component as network NBT: a plain string tag when it is only text, otherwise a compound. Writes
    /// nothing for <see langword="null"/>.
    /// </summary>
    /// <param name="chatMessage">The text to write.</param>
    public void WriteChat(ChatMessage chatMessage);

    /// <summary>
    /// Writes a compound as network NBT (no root name), the encoding of components vanilla doesn't sync with a dedicated
    /// codec, such as <c>minecraft:map_decorations</c>.
    /// </summary>
    public void WriteNbtCompound(Obsidian.Nbt.NbtCompound compound);
    /// <summary>
    /// Writes a compound as network NBT, or an empty (end) tag for <see langword="null"/>.
    /// </summary>
    /// <param name="compound">The compound to write.</param>
    public void WriteOptionalNbtCompound(Obsidian.Nbt.NbtCompound? compound);
    /// <summary>
    /// Writes the first <paramref name="size"/> bits of a bit set in <c>ceil(size / 8)</c> bytes, lowest bit first.
    /// </summary>
    /// <param name="bits">The bit set to write.</param>
    /// <param name="size">The number of bits.</param>
    public void WriteFixedBitSet(BitSet bits, int size);
    /// <summary>
    /// Writes a data component's value.
    /// </summary>
    /// <param name="component">The component to write.</param>
    public void WriteDataComponent(DataComponent component);
    /// <summary>
    /// Writes an item stack: its count, item and component changes, or an empty slot for <see langword="null"/> or air.
    /// </summary>
    /// <param name="itemStack">The stack to write.</param>
    public void WriteItemStack(ItemStack? itemStack);
    /// <summary>
    /// Writes an item stack with a hash of each component instead of the component, as clients send in container
    /// clicks.
    /// </summary>
    /// <param name="itemStack">The stack to write, or <see langword="null"/> for an empty slot.</param>
    public void WriteHashedItemStack(ItemStack? itemStack);
    /// <summary>
    /// Writes a hashed item stack as clients send it in container clicks.
    /// </summary>
    /// <param name="itemStack">The stack to write, or <see langword="null"/> for an empty slot.</param>
    public void WriteHashedItemStack(IHashedItemStack? itemStack);
    /// <summary>
    /// Writes an item stack with each component prefixed by its length, as creative-mode clients send it.
    /// </summary>
    /// <param name="itemStack">The stack to write, or <see langword="null"/> for an empty slot.</param>
    public void WriteUntrustedItemStack(ItemStack? itemStack);

    /// <summary>Writes a villager trade like vanilla's <c>MerchantOffer</c> stream codec.</summary>
    public void WriteMerchantOffer(TradeEntry value);
    /// <summary>
    /// Writes an item stack that can't be empty.
    /// </summary>
    /// <param name="itemStack">The stack to write.</param>
    /// <exception cref="ArgumentException"><paramref name="itemStack"/> is <see langword="null"/>, air or has no items.</exception>
    public void WriteRequiredItemStack(ItemStack itemStack);

    /// <summary>Writes a nonempty item template (item ID, count, component patch), used by nested components.</summary>
    public void WriteItemStackTemplate(ItemStack itemStack);
    /// <summary>
    /// Writes a VarInt count, then each item stack.
    /// </summary>
    /// <param name="values">The stacks to write.</param>
    public void WriteItemStackList(params ReadOnlySpan<ItemStack?> values);
    /// <summary>
    /// Writes a time as milliseconds since the Unix epoch, a 64-bit integer.
    /// </summary>
    /// <param name="date">The time to write.</param>
    public void WriteDateTimeOffset(DateTimeOffset date);
    /// <summary>
    /// Writes an inline sound event: its identifier and optional fixed range.
    /// </summary>
    /// <param name="soundEvent">The sound event to write.</param>
    public void WriteSoundEvent(SoundEvent soundEvent);
    /// <summary>
    /// Writes a sound effect's name in snake_case, then its fixed range as a float if it has one.
    /// </summary>
    /// <param name="sound">The sound to write.</param>
    public void WriteSoundEffect(SoundEffect sound);
    /// <summary>
    /// Writes bytes as they are, without a length prefix.
    /// </summary>
    /// <param name="values">The bytes to write.</param>
    public void WriteByteArray(byte[] values);
    /// <summary>
    /// Writes bytes as they are, without a length prefix.
    /// </summary>
    /// <param name="values">The bytes to write.</param>
    public void WriteByteArray(ReadOnlySpan<byte> values);
    /// <summary>
    /// Writes a UUID as two 64-bit integers, most significant first.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteUuid(Guid value);
    /// <summary>
    /// Writes a block position packed into a 64-bit integer (26 bits of x, 26 of z, 12 of y).
    /// </summary>
    /// <param name="value">The position to write.</param>
    public void WritePosition(Vector value);
    /// <summary>
    /// Writes a sound's position as three 32-bit integers.
    /// </summary>
    /// <param name="position">The position to write.</param>
    public void WritePosition(SoundPosition position);
    /// <summary>
    /// Writes a position as three floats: x, y and z.
    /// </summary>
    /// <param name="value">The position to write.</param>
    public void WriteAbsoluteFloatPosition(Vector value);
    /// <summary>
    /// Writes a position as three 16-bit integers: x, y and z.
    /// </summary>
    /// <param name="value">The position to write.</param>
    public void WriteAbsoluteShortPosition(Vector value);
    /// <summary>
    /// Writes a position as three 16-bit integers, truncating each coordinate.
    /// </summary>
    /// <param name="value">The position to write.</param>
    public void WriteAbsoluteShortPosition(VectorF value);
    /// <summary>
    /// Writes a position packed like <see cref="WritePosition(Vector)"/>, truncating each coordinate.
    /// </summary>
    /// <param name="value">The position to write.</param>
    public void WritePositionF(VectorF value);
    /// <summary>
    /// Writes a position as three floats: x, y and z.
    /// </summary>
    /// <param name="value">The position to write.</param>
    public void WriteAbsoluteFloatPositionF(VectorF value);
    /// <summary>
    /// Writes a velocity in vanilla's packed (LpVec3) format.
    /// </summary>
    /// <param name="value">The velocity to write.</param>
    public void WriteVelocity(Velocity value);
    /// <summary>
    /// Writes an advancement as the update advancements packet holds it.
    /// </summary>
    /// <param name="advancement">The advancement to write.</param>
    public void WriteAdvancement(Advancement advancement);

    /// <summary>
    /// Writes a VarInt count, each enchantment, then whether they show in tooltips.
    /// </summary>
    /// <param name="showInTooltips">Whether the enchantments show in the item's tooltip.</param>
    /// <param name="enchantments">The enchantments to write.</param>
    public void WriteLengthPrefixedArray(bool showInTooltips, params Enchantment[] enchantments);

    /// <summary>
    /// Writes a VarInt count, then each value with <paramref name="write"/>.
    /// </summary>
    /// <typeparam name="TValue">The element type.</typeparam>
    /// <param name="write">Writes one element.</param>
    /// <param name="values">The values to write.</param>
    public void WriteLengthPrefixedArray<TValue>(Action<TValue> write, params ReadOnlySpan<TValue> values);

    /// <summary>
    /// Writes an attribute modifier.
    /// </summary>
    /// <param name="attribute">The modifier to write.</param>
    public void WriteAttributeModifier(AttributeModifier attribute);

    /// <summary>
    /// Writes an enchantment's id and level, as VarInts.
    /// </summary>
    /// <param name="enchantment">The enchantment to write.</param>
    public void WriteEnchantment(Enchantment enchantment);

    //This needs further implementing (ICodec.Serialize)
    /// <summary>
    /// Writes a registry entry's data as a network NBT compound.
    /// </summary>
    /// <param name="codec">The entry to write.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void WriteCodec(ICodec codec);

    /// <summary>
    /// Writes a whole packet, with its length and id. A bundle is written as its packets between bundle delimiters.
    /// </summary>
    /// <param name="packet">The packet to write.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void WritePacket(IClientboundPacket packet);

    /// <summary>
    /// Writes a profile property: its name, value and optional signature.
    /// </summary>
    /// <param name="skinProperty">The property to write.</param>
    public void WriteSkinProperty(SkinProperty skinProperty);

    /// <summary>
    /// Writes whether <paramref name="value"/> has a value, then the value if it does.
    /// </summary>
    /// <typeparam name="TValue">The value's type.</typeparam>
    /// <param name="value">The value to write.</param>
    public void WriteOptional<TValue>(TValue? value) where TValue : struct, INetworkSerializable<TValue>;
    /// <summary>
    /// Writes whether <paramref name="value"/> is not <see langword="null"/>, then the value if it isn't.
    /// </summary>
    /// <typeparam name="TValue">The value's type.</typeparam>
    /// <param name="value">The value to write.</param>
    public void WriteOptional<TValue>(TValue? value) where TValue : INetworkSerializable<TValue>;
    /// <summary>
    /// Writes whether <paramref name="value"/> is not <see langword="null"/>, then its value as a VarInt if it isn't.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteOptional(Enum? value);
    /// <summary>
    /// Writes whether <paramref name="value"/> has a value, then the value as a 32-bit integer if it does.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteOptional(int? value);
    /// <summary>
    /// Writes whether <paramref name="value"/> has a value, then the value as a double if it does.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteOptional(double? value);
    /// <summary>
    /// Writes whether <paramref name="value"/> has a value, then the value as a 16-bit integer if it does.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteOptional(short? value);
    /// <summary>
    /// Writes whether <paramref name="value"/> has a value, then the value as a float if it does.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteOptional(float? value);
    /// <summary>
    /// Writes whether <paramref name="value"/> has a value, then the value as a byte if it does.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteOptional(byte? value);
    /// <summary>
    /// Writes whether <paramref name="value"/> has a value, then the value if it does.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteOptional(bool? value);
    /// <summary>
    /// Writes whether <paramref name="value"/> is not <see langword="null"/>, then the string if it isn't.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteOptional(string? value);
    /// <summary>
    /// Writes whether <paramref name="value"/> has a value, then the UUID if it does.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteOptional(Guid? value);
    /// <summary>
    /// Returns the stream's underlying byte array, which can be longer than <see cref="INetStream.Size"/>.
    /// </summary>
    public byte[] ToArray();
}
