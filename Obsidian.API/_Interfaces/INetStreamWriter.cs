using Obsidian.API.Advancements;
using Obsidian.API.Inventory;
using System.ComponentModel;

namespace Obsidian.API;
public interface INetStreamWriter : INetStream
{
    public bool CanWrite { get; }
    public void Write(INetStream buffer);
    public void WriteByte(sbyte value);
    public void WriteByte(Enum value);
    public void WriteByte(byte value);
    public void WriteBoolean(bool value);
    public void WriteUnsignedShort(ushort value);
    public void WriteShort(short value);

    public void WriteInt(int value);
    public void WriteInt(Enum value);
    public void WriteLong(long value);

    public void WriteSingle(float value);
    public void WriteDouble(double value);

    public void WriteString(string value, int maxLength = short.MaxValue);
    public void WriteVarInt(int value);
    public void WriteVarInt(Enum value);

    public void WriteLongArray(long[] values);
    public void WriteVarLong(long value);

    public void WriteEntityMetadataType(byte index, EntityMetadataType type);
    public void WriteEntity(IEntity entity);
    public void WriteBitSet(BitSet bitset, bool isFixed = false);
    public void WriteChat(ChatMessage chatMessage);
    public void WriteItemStack(ItemStack? itemStack);
    public void WriteDateTimeOffset(DateTimeOffset date);
    public void WriteSoundEvent(SoundEvent soundEvent);
    public void WriteSoundEffect(SoundEffect sound);
    public void WriteByteArray(byte[] values);
    public void WriteByteArray(Span<byte> values);
    public void WriteUuid(Guid value);
    public void WritePosition(Vector value);
    public void WritePosition(SoundPosition position);
    public void WriteAbsolutePosition(Vector value);
    public void WriteAbsoluteFloatPosition(Vector value);
    public void WriteAbsoluteShortPosition(Vector value);
    public void WriteAbsoluteShortPosition(VectorF value);
    public void WritePositionF(VectorF value);
    public void WriteAbsolutePositionF(VectorF value);
    public void WriteAbsoluteFloatPositionF(VectorF value);
    public void WriteVelocity(Velocity value);
    public void WriteAdvancement(Advancement advancement);

    public void WriteLengthPrefixedArray(bool showInTooltips, params Enchantment[] enchantments);

    public void WriteLengthPrefixedArray<TValue>(Action<TValue> write, params TValue[] values);

    public void WriteAttributeModifier(AttributeModifier attribute);

    public void WriteEnchantment(Enchantment enchantment);

    //This needs further implementing (ICodec.Serialize)
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void WriteCodec(ICodec codec);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void WritePacket(IClientboundPacket packet);

    public void WriteSkinProperty(SkinProperty skinProperty);

    public void WriteOptional<TValue>(TValue? value) where TValue : struct, INetworkSerializable<TValue>;
    public void WriteOptional<TValue>(TValue? value) where TValue : INetworkSerializable<TValue>;
    public void WriteOptional(Enum? value);
    public void WriteOptional(int? value);
    public void WriteOptional(double? value);
    public void WriteOptional(short? value);
    public void WriteOptional(float? value);
    public void WriteOptional(byte? value);
    public void WriteOptional(bool? value);
    public void WriteOptional(string? value);
    public void WriteOptional(Guid? value);
    public byte[] ToArray();
}
