using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.Chat;

public sealed record class ChatType : INbtSerializable
{
    public required List<string> Parameters { get; init; }

    public ChatStyle? Style { get; init; }

    public required string TranslationKey { get; init; }

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
