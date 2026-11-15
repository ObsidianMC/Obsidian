using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.Dialogs;

/// <summary>
/// Contains the common and type-specific fields of a client dialog definition.
/// </summary>
public sealed record class DialogElement : INbtSerializable
{
    /// <summary>
    /// Gets the vanilla dialog type identifier, validated by <see cref="Validate"/>.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Gets or sets the list or server-link button width from 1 through 1,024 client pixels.
    /// </summary>
    public int ButtonWidth { get; set; } = 200;

    /// <summary>
    /// Gets or sets the button column count; zero is serialized as the default of two.
    /// </summary>
    public int Columns { get; set; }

    /// <summary>
    /// Gets or sets the dialog reference or <c>#</c>-prefixed tag, taking precedence over <see cref="DialogIds"/>.
    /// </summary>
    public string? Dialogs { get; set; }

    /// <summary>
    /// Gets or sets the optional exit button for list, multi-action and server-link dialogs.
    /// </summary>
    public DialogAction? ExitAction { get; set; }

    /// <summary>
    /// Gets or sets the optional title displayed when another dialog references this one.
    /// </summary>
    public ChatMessage? ExternalTitle { get; set; }

    /// <summary>
    /// Gets or sets the dialog's displayed title.
    /// </summary>
    public required ChatMessage Title { get; set; }

    /// <summary>
    /// Gets or sets whether the client may close this dialog with Escape.
    /// </summary>
    public bool CanCloseWithEscape { get; set; } = true;
    /// <summary>
    /// Gets or sets whether displaying this dialog requests pausing the game.
    /// </summary>
    public bool Pause { get; set; } = true;
    /// <summary>
    /// Gets or sets <c>close</c>, <c>none</c> or <c>wait_for_response</c>; <c>none</c> requires pausing to be disabled.
    /// </summary>
    public string AfterAction { get; set; } = "close";
    /// <summary>
    /// Gets or sets the body entries in display order.
    /// </summary>
    public ImmutableArray<DialogBody> Body { get; set; } = [];
    /// <summary>
    /// Gets or sets the input definitions, whose keys must be unique within the dialog.
    /// </summary>
    public ImmutableArray<DialogInput> Inputs { get; set; } = [];
    /// <summary>
    /// Gets or sets the buttons of a multi-action dialog in display order.
    /// </summary>
    public ImmutableArray<DialogAction> Actions { get; set; } = [];
    /// <summary>
    /// Gets or sets the optional main button of a notice dialog.
    /// </summary>
    public DialogAction? Action { get; set; }
    /// <summary>
    /// Gets or sets the required affirmative button of a confirmation dialog.
    /// </summary>
    public DialogAction? Yes { get; set; }
    /// <summary>
    /// Gets or sets the required negative button of a confirmation dialog.
    /// </summary>
    public DialogAction? No { get; set; }
    /// <summary>
    /// Gets or sets explicit dialog identifiers used by a list when <see cref="Dialogs"/> is <c>null</c>.
    /// </summary>
    public ImmutableArray<string> DialogIds { get; set; } = [];

    /// <summary>
    /// Validates this dialog and writes its common and type-specific fields to NBT.
    /// </summary>
    public void Write(INbtWriter writer)
    {
        Validate();
        writer.WriteString("type", Type);
        DialogValidation.WriteText(writer, "title", Title);
        if (ExternalTitle is not null)
            DialogValidation.WriteText(writer, "external_title", ExternalTitle);
        writer.WriteBool("can_close_with_escape", CanCloseWithEscape);
        writer.WriteBool("pause", Pause);
        writer.WriteString("after_action", AfterAction);
        WriteList(writer, "body", Body);
        WriteList(writer, "inputs", Inputs);
        switch (Type)
        {
            case "minecraft:notice":
                WriteAction(writer, "action", Action);
                break;
            case "minecraft:confirmation":
                WriteAction(writer, "yes", Yes);
                WriteAction(writer, "no", No);
                break;
            case "minecraft:multi_action":
                WriteList(writer, "actions", Actions);
                goto case "minecraft:server_links";
            case "minecraft:dialog_list":
                if (Dialogs is not null)
                {
                    writer.WriteString("dialogs", Dialogs);
                }
                else
                {
                    writer.WriteListStart("dialogs", Obsidian.Nbt.NbtTagType.String, DialogIds.Length);
                    foreach (var id in DialogIds)
                        writer.WriteString(id);
                    writer.EndList();
                }
                goto case "minecraft:server_links";
            case "minecraft:server_links":
                writer.WriteInt("columns", Columns == 0 ? 2 : Columns);
                if (Type != "minecraft:multi_action")
                    writer.WriteInt("button_width", ButtonWidth);
                WriteAction(writer, "exit_action", ExitAction);
                break;
        }
    }

    /// <summary>
    /// Checks dialog type, behavior, dimensions, required buttons and references, throwing for invalid settings.
    /// </summary>
    public void Validate()
    {
        if (Type is not ("minecraft:notice" or "minecraft:confirmation" or "minecraft:multi_action" or "minecraft:server_links" or "minecraft:dialog_list"))
            throw new ArgumentException("Unknown vanilla dialog type.", nameof(Type));
        if (AfterAction is not ("close" or "none" or "wait_for_response") || (AfterAction == "none" && Pause))
            throw new ArgumentException("Invalid after_action; 'none' requires pause=false.");
        DialogValidation.Range(ButtonWidth, 1, 1024, nameof(ButtonWidth));
        DialogValidation.Range(Columns, 0, int.MaxValue, nameof(Columns));
        if (Type == "minecraft:confirmation" && (Yes is null || No is null))
            throw new ArgumentException("Confirmation dialogs require yes and no buttons.");
        if (Type == "minecraft:multi_action" && Actions.IsEmpty)
            throw new ArgumentException("Multi-action dialogs require at least one button.");
        if (Type == "minecraft:dialog_list" && Dialogs is null && DialogIds.IsEmpty)
            throw new ArgumentException("Dialog lists require a tag or dialog identifiers.");
        if (Dialogs is not null)
            DialogValidation.Identifier(Dialogs.StartsWith('#') ? Dialogs[1..] : Dialogs);
        foreach (var id in DialogIds)
            DialogValidation.Identifier(id);
        if (Inputs.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != Inputs.Length)
            throw new ArgumentException("Dialog input keys must be unique.");
    }

    private static void WriteAction(INbtWriter writer, string name, DialogAction? action)
    {
        if (action is null)
            return;
        writer.WriteCompoundStart(name);
        action.Write(writer);
        writer.EndCompound();
    }

    private static void WriteList<T>(INbtWriter writer, string name, ImmutableArray<T> entries) where T : INbtSerializable
    {
        if (entries.IsEmpty)
            return;
        writer.WriteListStart(name, Obsidian.Nbt.NbtTagType.Compound, entries.Length);
        foreach (var entry in entries)
        {
            writer.WriteCompoundStart();
            entry.Write(writer);
            writer.EndCompound();
        }
        writer.EndList();
    }
}
