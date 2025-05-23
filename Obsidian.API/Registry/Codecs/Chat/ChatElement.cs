using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.Chat;

public sealed record class ChatElement : INbtSerializable
{
    public required ChatType Chat { get; set; }

    public required ChatType Narration { get; set; }

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
