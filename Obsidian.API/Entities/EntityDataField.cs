using Obsidian.API.Inventory;

namespace Obsidian.API.Entities;

/// <summary>
/// A synced data field of a plugin's entity type, like a field of vanilla's <c>SynchedEntityData</c>: a value per
/// entity that the server sends modded Obsidian clients, read there through <c>IClientEntity.Metadata</c> at
/// <see cref="Index"/>. Make one with <see cref="EntityDataFields"/>, list it in
/// <see cref="EntityProperties.DataFields"/>, and read and write it with <see cref="IEntity.GetData{T}"/> and
/// <see cref="IEntity.SetData{T}"/>.
/// </summary>
/// <remarks>
/// Fields aren't saved, like vanilla's synced data: keep what must last in an attachment or saved data and set the
/// field from it.
/// </remarks>
public abstract class EntityDataField
{
    private protected EntityDataField(EntityMetadataType serializer) => this.Serializer = serializer;

    /// <summary>
    /// Vanilla's serializer the value is written with, which the client reads it with.
    /// </summary>
    public EntityMetadataType Serializer { get; }

    /// <summary>
    /// The field's metadata index: after the fields vanilla sends for the type's kind (16 for a mob, 8 otherwise), in
    /// the order of <see cref="EntityProperties.DataFields"/>. Set when a type with the field is registered; -1
    /// before.
    /// </summary>
    public int Index { get; internal set; } = -1;

    internal abstract object? DefaultValue { get; }

    internal abstract void Write(INetStreamWriter writer, object? value);
}

/// <summary>
/// A synced data field holding a <typeparamref name="T"/>.
/// </summary>
public sealed class EntityDataField<T> : EntityDataField
{
    private readonly Action<INetStreamWriter, T> write;

    internal EntityDataField(EntityMetadataType serializer, T defaultValue, Action<INetStreamWriter, T> write)
        : base(serializer)
    {
        this.Default = defaultValue;
        this.write = write;
    }

    /// <summary>
    /// The value entities start with.
    /// </summary>
    public T Default { get; }

    internal override object? DefaultValue => this.Default;

    internal override void Write(INetStreamWriter writer, object? value) => this.write(writer, (T)value!);
}

/// <summary>
/// Makes synced data fields with vanilla's serializers, which modded clients read without knowing the plugin.
/// </summary>
public static class EntityDataFields
{
    public static EntityDataField<byte> Byte(byte defaultValue = 0) =>
        new(EntityMetadataType.Byte, defaultValue, (writer, value) => writer.WriteByte(value));

    public static EntityDataField<int> Int(int defaultValue = 0) =>
        new(EntityMetadataType.VarInt, defaultValue, (writer, value) => writer.WriteVarInt(value));

    public static EntityDataField<long> Long(long defaultValue = 0) =>
        new(EntityMetadataType.VarLong, defaultValue, (writer, value) => writer.WriteVarLong(value));

    public static EntityDataField<float> Float(float defaultValue = 0) =>
        new(EntityMetadataType.Float, defaultValue, (writer, value) => writer.WriteSingle(value));

    public static EntityDataField<string> String(string defaultValue = "") =>
        new(EntityMetadataType.String, defaultValue, (writer, value) => writer.WriteString(value));

    public static EntityDataField<bool> Boolean(bool defaultValue = false) =>
        new(EntityMetadataType.Boolean, defaultValue, (writer, value) => writer.WriteBoolean(value));

    /// <summary>
    /// An item stack field; clients read an empty stack as air.
    /// </summary>
    public static EntityDataField<ItemStack?> Item(ItemStack? defaultValue = null) =>
        new(EntityMetadataType.Slot, defaultValue, (writer, value) => writer.WriteItemStack(value));

    public static EntityDataField<Vector> BlockPos(Vector defaultValue = default) =>
        new(EntityMetadataType.BlockPos, defaultValue, (writer, value) => writer.WritePosition(value));
}
