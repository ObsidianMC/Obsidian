using Obsidian.API.Utilities;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.Dialogs;
public sealed record class DialogAction : INbtSerializable
{
    public required ChatMessage Label { get; set; }

    public required int Width { get; set; }

    public ChatMessage? Tooltip { get; set; }
    public DialogClickAction? Action { get; set; }

    public static DialogAction Create(ChatMessage label, DialogClickAction? action = null, int width = 150) =>
        new() { Label = label, Action = action, Width = width };

    public void Write(INbtWriter writer)
    {
        DialogValidation.Range(Width, 1, 1024, nameof(Width));
        writer.WriteInt("width", Width);

        writer.WriteCompoundStart("label");

        writer.WriteChatMessage(this.Label);

        writer.EndCompound();
        if (Tooltip is not null) DialogValidation.WriteText(writer, "tooltip", Tooltip);
        if (Action is not null)
        {
            writer.WriteCompoundStart("action");
            Action.Write(writer);
            writer.EndCompound();
        }
    }
}
