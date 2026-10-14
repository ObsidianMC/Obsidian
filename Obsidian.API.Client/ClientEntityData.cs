using Obsidian.API.Entities;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Client;

/// <summary>
/// Reads a plugin entity type's synced data fields (see <see cref="EntityProperties.DataFields"/>) from the metadata
/// the client received.
/// </summary>
public static class ClientEntityData
{
    /// <summary>
    /// The field's value on the entity, if the server sent it: the Obsidian server sends a type's fields to the
    /// Obsidian client, at the field's <see cref="EntityDataField.Index"/>, when both registered the type with them.
    /// </summary>
    public static bool TryGetData<T>(
        this IClientEntity entity,
        EntityDataField<T> field,
        [MaybeNullWhen(false)] out T value)
    {
        if (field.Index >= 0 && entity.Metadata.TryGetValue((byte)field.Index, out var data) && data is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }
}
