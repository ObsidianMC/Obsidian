using Obsidian.API.Registry.Codecs.Dialogs;
using Obsidian.API.Registries;
using Obsidian.Nbt;

namespace Obsidian.API.Builders;

public sealed class DialogBuilder
{
    private readonly DialogElement dialog;
    private DialogBuilder(string type, ChatMessage title) => dialog = new() { Type = type, Title = title };

    public static DialogBuilder Notice(ChatMessage title, DialogAction? action = null) =>
        new DialogBuilder("minecraft:notice", title).WithAction(action ?? DialogAction.Create("OK"));
    public static DialogBuilder Confirmation(ChatMessage title, DialogAction yes, DialogAction no) =>
        new("minecraft:confirmation", title) { dialog = { Yes = yes, No = no } };
    public static DialogBuilder MultiAction(ChatMessage title) => new("minecraft:multi_action", title);
    public static DialogBuilder ServerLinks(ChatMessage title) => new("minecraft:server_links", title);
    public static DialogBuilder DialogList(ChatMessage title, params string[] ids) =>
        new("minecraft:dialog_list", title) { dialog = { DialogIds = [.. ids] } };
    public static DialogBuilder DialogTag(ChatMessage title, string tag) =>
        new("minecraft:dialog_list", title) { dialog = { Dialogs = tag.StartsWith('#') ? tag : $"#{tag}" } };

    public DialogBuilder WithExternalTitle(ChatMessage title) { dialog.ExternalTitle = title; return this; }
    public DialogBuilder WithBody(params DialogBody[] body) { dialog.Body = dialog.Body.AddRange(body); return this; }
    public DialogBuilder WithMessage(ChatMessage message, int width = 200) => WithBody(DialogBody.Message(message, width));
    public DialogBuilder WithInput(params DialogInput[] inputs) { dialog.Inputs = dialog.Inputs.AddRange(inputs); return this; }
    public DialogBuilder WithAction(DialogAction action)
    {
        if (dialog.Type != "minecraft:notice") throw new InvalidOperationException("Use WithActions for multi-action dialogs.");
        dialog.Action = action; return this;
    }
    public DialogBuilder WithActions(params DialogAction[] actions)
    {
        if (dialog.Type != "minecraft:multi_action") throw new InvalidOperationException("Only multi-action dialogs have an action list.");
        dialog.Actions = dialog.Actions.AddRange(actions); return this;
    }
    public DialogBuilder WithExitAction(DialogAction action)
    {
        if (dialog.Type is "minecraft:notice" or "minecraft:confirmation")
            throw new InvalidOperationException("This dialog's main button already defines its exit action.");
        dialog.ExitAction = action; return this;
    }
    public DialogBuilder WithColumns(int columns)
    {
        DialogValidation.Range(columns, 1, int.MaxValue, nameof(columns));
        dialog.Columns = columns; return this;
    }
    public DialogBuilder WithButtonWidth(int width)
    {
        DialogValidation.Range(width, 1, 1024, nameof(width));
        dialog.ButtonWidth = width; return this;
    }
    public DialogBuilder WithEscape(bool allowed = true) { dialog.CanCloseWithEscape = allowed; return this; }
    public DialogBuilder WithPause(bool pause = true) { dialog.Pause = pause; return this; }
    public DialogBuilder AfterAction(string behavior) { dialog.AfterAction = behavior; return this; }

    public DialogElement Build()
    {
        var result = dialog with { };
        // Validate nested buttons and input codecs before anything is queued to a client.
        using var writer = new RawNbtWriter(true);
        result.Write(writer);
        writer.EndCompound();
        writer.TryFinish();
        return result;
    }

    /// <summary>Register before players connect; registry changes take effect on their next connection.</summary>
    public DialogElement Register(string id, bool quickAction = false, bool pauseScreen = false)
    {
        var result = Build();
        DialogRegistry.Register(id, result, quickAction, pauseScreen);
        return result;
    }
}
