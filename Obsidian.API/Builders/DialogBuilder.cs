using Obsidian.API.Registry.Codecs.Dialogs;
using Obsidian.API.Registries;
using Obsidian.Nbt;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Obsidian.API.Builders;

/// <summary>
/// Builds and validates a client dialog definition before use or registration.
/// </summary>
public sealed class DialogBuilder
{
    private readonly DialogElement dialog;
    private DialogBuilder(string type, ChatMessage title) => dialog = new() { Type = type, Title = title };

    /// <summary>
    /// Creates a notice with one action, defaulting to an <c>OK</c> button when none is supplied.
    /// </summary>
    public static DialogBuilder Notice(ChatMessage title, DialogAction? action = null) =>
        new DialogBuilder("minecraft:notice", title).WithAction(action ?? DialogAction.Create("OK"));

    /// <summary>
    /// Creates a confirmation dialog with separate affirmative and negative actions.
    /// </summary>
    public static DialogBuilder Confirmation(ChatMessage title, DialogAction yes, DialogAction no) =>
        new("minecraft:confirmation", title) { dialog = { Yes = yes, No = no } };

    /// <summary>
    /// Creates a dialog whose buttons are added with <see cref="WithActions"/>.
    /// </summary>
    public static DialogBuilder MultiAction(ChatMessage title) => new("minecraft:multi_action", title);
    /// <summary>
    /// Creates a dialog displaying the server's configured links.
    /// </summary>
    public static DialogBuilder ServerLinks(ChatMessage title) => new("minecraft:server_links", title);

    /// <summary>
    /// Creates a dialog listing the supplied registered dialog identifiers.
    /// </summary>
    public static DialogBuilder DialogList(ChatMessage title, params string[] ids) =>
        new("minecraft:dialog_list", title) { dialog = { DialogIds = [.. ids] } };

    /// <summary>
    /// Creates a dialog listing a dialog tag, adding the <c>#</c> prefix when needed.
    /// </summary>
    public static DialogBuilder DialogTag(ChatMessage title, string tag) =>
        new("minecraft:dialog_list", title) { dialog = { Dialogs = tag.StartsWith('#') ? tag : $"#{tag}" } };

    /// <summary>
    /// Sets the title shown when this dialog is referenced from another dialog.
    /// </summary>
    public DialogBuilder WithExternalTitle(ChatMessage title)
    {
        dialog.ExternalTitle = title;
        return this;
    }

    /// <summary>
    /// Appends body entries in display order and returns this builder.
    /// </summary>
    public DialogBuilder WithBody(params DialogBody[] body)
    {
        dialog.Body = dialog.Body.AddRange(body);
        return this;
    }

    /// <summary>
    /// Appends a text body entry with the specified width in client pixels.
    /// </summary>
    public DialogBuilder WithMessage(ChatMessage message, int width = 200) => WithBody(DialogBody.Message(message, width));

    /// <summary>
    /// Appends input definitions in display order and returns this builder.
    /// </summary>
    public DialogBuilder WithInput(params DialogInput[] inputs)
    {
        dialog.Inputs = dialog.Inputs.AddRange(inputs);
        return this;
    }

    /// <summary>
    /// Sets the main button of a notice dialog; throws <see cref="InvalidOperationException"/> for other types.
    /// </summary>
    public DialogBuilder WithAction(DialogAction action)
    {
        if (dialog.Type != "minecraft:notice")
            throw new InvalidOperationException("Use WithActions for multi-action dialogs.");
        dialog.Action = action;
        return this;
    }

    /// <summary>
    /// Appends buttons to a multi-action dialog; throws <see cref="InvalidOperationException"/> for other types.
    /// </summary>
    public DialogBuilder WithActions(params DialogAction[] actions)
    {
        if (dialog.Type != "minecraft:multi_action")
            throw new InvalidOperationException("Only multi-action dialogs have an action list.");
        dialog.Actions = dialog.Actions.AddRange(actions);
        return this;
    }

    /// <summary>
    /// Sets the exit button; throws <see cref="InvalidOperationException"/> for notices and confirmations.
    /// </summary>
    public DialogBuilder WithExitAction(DialogAction action)
    {
        if (dialog.Type is "minecraft:notice" or "minecraft:confirmation")
            throw new InvalidOperationException("This dialog's main button already defines its exit action.");
        dialog.ExitAction = action;
        return this;
    }

    /// <summary>
    /// Sets the positive button column count and returns this builder.
    /// </summary>
    public DialogBuilder WithColumns(int columns)
    {
        DialogValidation.Range(columns, 1, int.MaxValue, nameof(columns));
        dialog.Columns = columns;
        return this;
    }

    /// <summary>
    /// Sets button width from 1 through 1,024 client pixels and returns this builder.
    /// </summary>
    public DialogBuilder WithButtonWidth(int width)
    {
        DialogValidation.Range(width, 1, 1024, nameof(width));
        dialog.ButtonWidth = width;
        return this;
    }

    /// <summary>
    /// Sets whether the client may close this dialog with Escape.
    /// </summary>
    public DialogBuilder WithEscape(bool allowed = true)
    {
        dialog.CanCloseWithEscape = allowed;
        return this;
    }

    /// <summary>
    /// Sets whether the dialog requests pausing the game.
    /// </summary>
    public DialogBuilder WithPause(bool pause = true)
    {
        dialog.Pause = pause;
        return this;
    }

    /// <summary>
    /// Sets the post-action behavior identifier, validated when the dialog is built.
    /// </summary>
    public DialogBuilder AfterAction(string behavior)
    {
        dialog.AfterAction = behavior;
        return this;
    }

    /// <summary>
    /// Copies the dialog and validates it by writing its NBT representation, throwing for invalid settings.
    /// </summary>
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

    /// <summary>
    /// Builds the dialog and registers it with
    /// <see cref="DialogRegistry.Register(string, DialogElement, bool, bool)"/>.
    /// </summary>
    /// <remarks>
    /// Register before players connect: players playing get changed dialogs when plugins load or unload. A plugin's
    /// dialogs are removed when it unloads.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public DialogElement Register(string id, bool quickAction = false, bool pauseScreen = false)
    {
        var result = Build();
        DialogRegistry.Register(id, result, quickAction, pauseScreen, Assembly.GetCallingAssembly());
        return result;
    }
}
