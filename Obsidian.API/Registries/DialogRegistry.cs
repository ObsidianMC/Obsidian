using Obsidian.API.Registry.Codecs.Dialogs;
using Obsidian.Nbt;

namespace Obsidian.API.Registries;

/// <summary>Custom dialogs synchronized at login. Register them during plugin initialization.</summary>
public static class DialogRegistry
{
    private static readonly object sync = new();
    private static readonly Dictionary<string, DialogRegistration> entries = new(StringComparer.Ordinal);

    public static void Register(string id, DialogElement dialog, bool quickAction = false, bool pauseScreen = false)
    {
        DialogValidation.Identifier(id);
        if (id.StartsWith("minecraft:", StringComparison.Ordinal) || id == "obsidian:feedback")
            throw new ArgumentException("This dialog identifier is reserved.", nameof(id));
        using var writer = new RawNbtWriter(true);
        dialog.Write(writer);
        writer.EndCompound();
        writer.TryFinish();
        lock (sync) entries.Add(id, new(id, dialog with { }, quickAction, pauseScreen));
    }

    public static bool Unregister(string id)
    {
        lock (sync) return entries.Remove(id);
    }

    public static ImmutableArray<DialogRegistration> GetRegistrations()
    {
        lock (sync) return [.. entries.Values.OrderBy(x => x.Id, StringComparer.Ordinal)];
    }
}

public sealed record DialogRegistration(string Id, DialogElement Dialog, bool QuickAction, bool PauseScreen);
