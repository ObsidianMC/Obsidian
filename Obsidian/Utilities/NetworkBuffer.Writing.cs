using Obsidian.API.Advancements;
using Obsidian.API.Inventory;

namespace Obsidian.Utilities;
public partial class NetworkBuffer : INetStreamWriter
{
    public bool CanWrite { get; set; }

    public byte[] ToArray() => this.Data;
    public void WriteAbsoluteFloatPosition(Vector value) => throw new NotImplementedException();
    public void WriteAbsoluteFloatPositionF(VectorF value) => throw new NotImplementedException();
    public void WriteAbsolutePosition(Vector value) => throw new NotImplementedException();
    public void WriteAbsolutePositionF(VectorF value) => throw new NotImplementedException();
    public void WriteAbsoluteShortPosition(Vector value) => throw new NotImplementedException();
    public void WriteAbsoluteShortPosition(VectorF value) => throw new NotImplementedException();
    public void WriteAdvancement(Advancement advancement) => throw new NotImplementedException();
    public void WriteAttributeModifier(AttributeModifier attribute) => throw new NotImplementedException();
    public void WriteBitSet(BitSet bitset, bool isFixed = false) => throw new NotImplementedException();
    public unsafe void WriteBoolean(bool value) => this.WriteByte(*(byte*)&value);
    public void WriteByte(sbyte value) => this.WriteByte((byte)value);
    public void WriteByte(Enum value) => this.WriteByte((byte)value.GetHashCode());
    public void WriteByteArray(byte[] values) => throw new NotImplementedException();
    public void WriteChat(ChatMessage chatMessage) => throw new NotImplementedException();
    public void WriteCodec(ICodec codec) => throw new NotImplementedException();
    public void WriteDateTimeOffset(DateTimeOffset date) => throw new NotImplementedException();
    public void WriteDouble(double value) => throw new NotImplementedException();
    public void WriteEnchantment(Enchantment enchantment) => throw new NotImplementedException();
    public void WriteEntity(IEntity entity) => throw new NotImplementedException();
    public void WriteEntityMetadataType(byte index, EntityMetadataType type) => throw new NotImplementedException();
    public void WriteInt(int value) => throw new NotImplementedException();
    public void WriteInt(Enum value) => throw new NotImplementedException();
    public void WriteItemStack(ItemStack? itemStack) => throw new NotImplementedException();
    public void WriteLengthPrefixedArray(bool showInTooltips, params List<Enchantment> enchantments) => throw new NotImplementedException();
    public void WriteLengthPrefixedArray<TValue>(Action<TValue> write, params List<TValue> values) => throw new NotImplementedException();
    public void WriteLong(long value) => throw new NotImplementedException();
    public void WriteLongArray(long[] values) => throw new NotImplementedException();
    public void WriteOptional<TValue>(TValue? value) where TValue : struct, INetworkSerializable<TValue> => throw new NotImplementedException();
    public void WriteOptional<TValue>(TValue? value) where TValue : INetworkSerializable<TValue> => throw new NotImplementedException();
    public void WriteOptional(Enum? value) => throw new NotImplementedException();
    public void WriteOptional(int? value) => throw new NotImplementedException();
    public void WriteOptional(double? value) => throw new NotImplementedException();
    public void WriteOptional(short? value) => throw new NotImplementedException();
    public void WriteOptional(float? value) => throw new NotImplementedException();
    public void WriteOptional(byte? value) => throw new NotImplementedException();
    public void WriteOptional(bool? value) => throw new NotImplementedException();
    public void WriteOptional(string? value) => throw new NotImplementedException();
    public void WriteOptional(Guid? value) => throw new NotImplementedException();
    public void WritePacket(IClientboundPacket packet) => throw new NotImplementedException();
    public void WritePosition(Vector value) => throw new NotImplementedException();
    public void WritePosition(SoundPosition position) => throw new NotImplementedException();
    public void WritePositionF(VectorF value) => throw new NotImplementedException();
    public void WriteShort(short value) => throw new NotImplementedException();
    public void WriteSingle(float value) => throw new NotImplementedException();
    public void WriteSkinProperty(SkinProperty skinProperty) => throw new NotImplementedException();
    public void WriteSoundEffect(SoundEffect sound) => throw new NotImplementedException();
    public void WriteSoundEvent(SoundEvent soundEvent) => throw new NotImplementedException();
    public void WriteString(string value, int maxLength = 32767) => throw new NotImplementedException();
    public void WriteUnsignedShort(ushort value) => throw new NotImplementedException();
    public void WriteUuid(Guid value) => throw new NotImplementedException();
    public void WriteVarInt(int value) => throw new NotImplementedException();
    public void WriteVarInt(Enum value) => throw new NotImplementedException();
    public void WriteVarLong(long value) => throw new NotImplementedException();
    public void WriteVelocity(Velocity value) => throw new NotImplementedException();
}
