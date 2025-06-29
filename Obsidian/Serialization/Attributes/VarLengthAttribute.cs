namespace Obsidian.Serialization.Attributes;

/// <summary>
/// Indicates that the field or property is in VarInt or VarLong format, or the method returns a VarInt
/// or VarLong value. See <see href="https://minecraft.wiki/w/Minecraft_Wiki:Projects/wiki.vg_merge/Data_types#VarInt_and_VarLong"/>.
/// The applied field or property must be of type <see cref="int"/> or <see cref="long"/> and the applied
/// method must return an <see cref="int"/> or <see cref="long"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class VarLengthAttribute : Attribute { }
