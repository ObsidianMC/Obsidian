using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.Chat;

/// <summary>
/// Defines a chat translation key, ordered substitution parameters and optional style.
/// </summary>
public sealed record class ChatType : INbtSerializable
{
    /// <summary>
    /// Gets the ordered substitution names, such as <c>sender</c> and <c>content</c>.
    /// </summary>
    public required List<string> Parameters { get; init; }

    /// <summary>
    /// Gets the optional formatting style; <c>null</c> omits the <c>style</c> compound.
    /// </summary>
    public ChatStyle? Style { get; init; }

    /// <summary>
    /// Gets the client translation key, serialized as <c>translation_key</c>.
    /// </summary>
    public required string TranslationKey { get; init; }

    /// <summary>
    /// Writes the substitution parameter list, optional style and translation key to NBT.
    /// </summary>
    public void Write(INbtWriter writer)
    {
        var parameters = new NbtList(NbtTagType.String, "parameters");

        foreach (var param in this.Parameters)
            parameters.Add(new NbtTag<string>("", param));

        if (this.Style is ChatStyle style)
        {
            writer.WriteCompoundStart("style");

            writer.WriteString("color", style.Color);
            writer.WriteBool("italic", style.Italic);

            writer.EndCompound();
        }

        writer.WriteTag(parameters);
        writer.WriteString("translation_key", this.TranslationKey);
    }
}
