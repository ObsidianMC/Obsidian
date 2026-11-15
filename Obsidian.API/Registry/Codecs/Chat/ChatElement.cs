using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.Chat;

/// <summary>
/// Contains separate visible chat and narration formatting definitions.
/// </summary>
public sealed record class ChatElement : INbtSerializable
{
    /// <summary>
    /// Gets or sets the formatting serialized in the <c>chat</c> compound.
    /// </summary>
    public required ChatType Chat { get; set; }

    /// <summary>
    /// Gets or sets the formatting serialized in the <c>narration</c> compound.
    /// </summary>
    public required ChatType Narration { get; set; }

    /// <summary>
    /// Writes separate <c>chat</c> and <c>narration</c> formatting compounds to NBT.
    /// </summary>
    public void Write(INbtWriter writer)
    {
        writer.WriteCompoundStart("chat");

        this.Chat.Write(writer);

        writer.EndCompound();

        writer.WriteCompoundStart("narration");

        this.Narration.Write(writer);

        writer.EndCompound();
    }
}
