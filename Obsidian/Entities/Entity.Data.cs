using Obsidian.API.Entities;
using Obsidian.Net;
using System.Threading;

namespace Obsidian.Entities;

// The synced data fields of a plugin's entity type (EntityProperties.DataFields), sent after vanilla's fields to
// modded Obsidian clients only: other clients would fail on indices their entity classes don't have.
public partial class Entity
{
    private readonly Lock dataLock = new();

    // The values of the type's data fields, in their order; null while every field has its default.
    private object?[]? dataValues;

    /// <summary>
    /// Whether a data field changed since the entity's data was last sent; the entity's tick sends it.
    /// </summary>
    internal bool DataChanged { get; set; }

    public T GetData<T>(EntityDataField<T> field)
    {
        var slot = this.DataSlot(field);

        lock (this.dataLock)
            return this.dataValues is object?[] values ? (T)values[slot]! : field.Default;
    }

    public void SetData<T>(EntityDataField<T> field, T value)
    {
        var slot = this.DataSlot(field);

        lock (this.dataLock)
        {
            var values = this.dataValues ??= [.. this.Definition!.Settings.DataFields.Select(f => f.DefaultValue)];
            if (EqualityComparer<T>.Default.Equals((T)values[slot]!, value))
                return;

            values[slot] = value;
            this.DataChanged = true;
        }
    }

    /// <summary>
    /// Writes the type's data fields after vanilla's, if the connection reads them (see
    /// <see cref="NetworkBuffer.WritesPluginEntityData"/>).
    /// </summary>
    protected void WritePluginData(INetStreamWriter writer)
    {
        if (writer is not NetworkBuffer { WritesPluginEntityData: true })
            return;

        if (this.Definition is not EntityDefinition type)
            return;

        var fields = type.Settings.DataFields;

        lock (this.dataLock)
        {
            var values = this.dataValues?.Length == fields.Length ? this.dataValues : null;

            for (var i = 0; i < fields.Length; i++)
            {
                writer.WriteEntityMetadataType((byte)fields[i].Index, fields[i].Serializer);
                fields[i].Write(writer, values is null ? fields[i].DefaultValue : values[i]);
            }
        }
    }

    private int DataSlot(EntityDataField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        var slot = this.Definition?.Settings.IndexOf(field) ?? -1;

        return slot >= 0
            ? slot
            : throw new ArgumentException("The entity's type doesn't have the field.", nameof(field));
    }
}
