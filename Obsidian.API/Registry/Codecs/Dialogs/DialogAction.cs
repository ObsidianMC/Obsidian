using Obsidian.API.Utilities;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.Dialogs;
public sealed record class DialogAction : INbtSerializable
{
    public required ChatMessage Label { get; set; }

    public required int Width { get; set; }

    public void Write(INbtWriter writer)
    {
        writer.WriteInt("width", Width);

        writer.WriteCompoundStart("label");

        writer.WriteChatMessage(this.Label);

        writer.EndCompound();
    }
}
