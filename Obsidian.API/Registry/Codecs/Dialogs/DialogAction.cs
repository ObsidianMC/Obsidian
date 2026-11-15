using Obsidian.API.Utilities;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.Dialogs;
/// <summary>
/// Defines a dialog button's label, width, tooltip and optional click behavior.
/// </summary>
public sealed record class DialogAction : INbtSerializable
{
    /// <summary>
    /// Gets or sets the text displayed on the button.
    /// </summary>
    public required ChatMessage Label { get; set; }

    /// <summary>
    /// Gets or sets the button width from 1 through 1,024 client pixels.
    /// </summary>
    public required int Width { get; set; }

    /// <summary>
    /// Gets or sets the hover text, or <c>null</c> to omit it.
    /// </summary>
    public ChatMessage? Tooltip { get; set; }
    /// <summary>
    /// Gets or sets the click behavior, or <c>null</c> to omit the action payload.
    /// </summary>
    public DialogClickAction? Action { get; set; }

    /// <summary>
    /// Creates a button with the supplied label, optional click behavior and width in client pixels.
    /// </summary>
    public static DialogAction Create(ChatMessage label, DialogClickAction? action = null, int width = 150) =>
        new() { Label = label, Action = action, Width = width };

    /// <summary>
    /// Validates the button width and writes the label, optional tooltip and click action to NBT.
    /// </summary>
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
