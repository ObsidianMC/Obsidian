using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.Dialogs;

public sealed record class DialogElement : INbtSerializable
{
    public required string Type { get; init; }

    public int ButtonWidth { get; set; } = 200;

    public int Columns { get; set; }

    public string? Dialogs { get; set; }

    public DialogAction? ExitAction { get; set; }

    public ChatMessage? ExternalTitle { get; set; }

    public required ChatMessage Title { get; set; }

    public bool CanCloseWithEscape { get; set; } = true;
    public bool Pause { get; set; } = true;
    public string AfterAction { get; set; } = "close";
    public ImmutableArray<DialogBody> Body { get; set; } = [];
    public ImmutableArray<DialogInput> Inputs { get; set; } = [];
    public ImmutableArray<DialogAction> Actions { get; set; } = [];
    public DialogAction? Action { get; set; }
    public DialogAction? Yes { get; set; }
    public DialogAction? No { get; set; }
    public ImmutableArray<string> DialogIds { get; set; } = [];

    public void Write(INbtWriter writer)
    {
        Validate();
        writer.WriteString("type", Type);
        DialogValidation.WriteText(writer, "title", Title);
        if (ExternalTitle is not null) DialogValidation.WriteText(writer, "external_title", ExternalTitle);
        writer.WriteBool("can_close_with_escape", CanCloseWithEscape);
        writer.WriteBool("pause", Pause);
        writer.WriteString("after_action", AfterAction);
        WriteList(writer, "body", Body);
        WriteList(writer, "inputs", Inputs);
        switch (Type)
        {
            case "minecraft:notice": WriteAction(writer, "action", Action); break;
            case "minecraft:confirmation":
                WriteAction(writer, "yes", Yes); WriteAction(writer, "no", No); break;
            case "minecraft:multi_action":
                WriteList(writer, "actions", Actions);
                goto case "minecraft:server_links";
            case "minecraft:dialog_list":
                if (Dialogs is not null) writer.WriteString("dialogs", Dialogs);
                else
                {
                    writer.WriteListStart("dialogs", Obsidian.Nbt.NbtTagType.String, DialogIds.Length);
                    foreach (var id in DialogIds) writer.WriteString(id);
                    writer.EndList();
                }
                goto case "minecraft:server_links";
            case "minecraft:server_links":
                writer.WriteInt("columns", Columns == 0 ? 2 : Columns);
                if (Type != "minecraft:multi_action") writer.WriteInt("button_width", ButtonWidth);
                WriteAction(writer, "exit_action", ExitAction);
                break;
        }
    }

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
        if (Dialogs is not null) DialogValidation.Identifier(Dialogs.StartsWith('#') ? Dialogs[1..] : Dialogs);
        foreach (var id in DialogIds) DialogValidation.Identifier(id);
        if (Inputs.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != Inputs.Length)
            throw new ArgumentException("Dialog input keys must be unique.");
    }

    private static void WriteAction(INbtWriter writer, string name, DialogAction? action)
    {
        if (action is null) return;
        writer.WriteCompoundStart(name); action.Write(writer); writer.EndCompound();
    }

    private static void WriteList<T>(INbtWriter writer, string name, ImmutableArray<T> entries) where T : INbtSerializable
    {
        if (entries.IsEmpty) return;
        writer.WriteListStart(name, Obsidian.Nbt.NbtTagType.Compound, entries.Length);
        foreach (var entry in entries)
        {
            writer.WriteCompoundStart(); entry.Write(writer); writer.EndCompound();
        }
        writer.EndList();
    }
}
