using Obsidian.API.Registry.Codecs.Dialogs;
using Obsidian.Nbt;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Obsidian.API.Registries;

/// <summary>
/// Custom dialogs synchronized when players configure. Register them during plugin initialization.
/// </summary>
/// <remarks>
/// A plugin's dialogs are removed when it unloads. Players already playing get the changed dialogs when plugins
/// load or unload, which sends them back through configuration.
/// </remarks>
public static class DialogRegistry
{
    private static readonly Lock sync = new();
    private static readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);

    // Plugins' load contexts whose dialogs were released, which may not register more.
    private static readonly ConditionalWeakTable<AssemblyLoadContext, object> released = [];

    /// <summary>
    /// Identifies the registered dialogs and their contents, so a client can tell it was configured with others.
    /// </summary>
    internal static string Fingerprint { get; private set; } = "";

    /// <summary>
    /// Registers a dialog, sent to players when they configure.
    /// </summary>
    /// <param name="id">The dialog's identifier, outside the <c>minecraft</c> namespace.</param>
    /// <param name="dialog">The dialog.</param>
    /// <param name="quickAction">Whether the quick actions key opens it.</param>
    /// <param name="pauseScreen">Whether the pause menu links to it.</param>
    /// <exception cref="ArgumentException">The identifier is invalid, reserved or registered.</exception>
    /// <exception cref="InvalidOperationException">The registering plugin is unloading.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Register(string id, DialogElement dialog, bool quickAction = false, bool pauseScreen = false) =>
        Register(id, dialog, quickAction, pauseScreen, Assembly.GetCallingAssembly());

    /// <summary>
    /// Registers a dialog for <paramref name="caller"/>, whose plugin owns it when it's a plugin's code.
    /// </summary>
    internal static void Register(
        string id,
        DialogElement dialog,
        bool quickAction,
        bool pauseScreen,
        Assembly caller)
    {
        DialogValidation.Identifier(id);

        if (id.StartsWith("minecraft:", StringComparison.Ordinal) || id == "obsidian:feedback")
            throw new ArgumentException("This dialog identifier is reserved.", nameof(id));

        using var writer = new RawNbtWriter(true);
        dialog.Write(writer);
        writer.EndCompound();
        writer.TryFinish();

        // Plugins load into collectible contexts; the server's own code doesn't.
        var context = AssemblyLoadContext.GetLoadContext(caller);
        var owner = context is { IsCollectible: true } ? context : null;
        var registration = new DialogRegistration(id, dialog with { }, quickAction, pauseScreen);

        lock (sync)
        {
            if (owner is not null && released.TryGetValue(owner, out _))
                throw new InvalidOperationException("The plugin registering the dialog is unloading.");

            entries.Add(id, new Entry(registration, owner, writer.Data.ToArray()));
            Update();
        }
    }

    /// <summary>
    /// Removes a dialog.
    /// </summary>
    /// <returns>Whether it was registered.</returns>
    public static bool Unregister(string id)
    {
        lock (sync)
        {
            if (!entries.Remove(id))
                return false;

            Update();
            return true;
        }
    }

    /// <summary>
    /// The registered dialogs, by identifier.
    /// </summary>
    public static ImmutableArray<DialogRegistration> GetRegistrations()
    {
        lock (sync)
        {
            return
            [
                .. entries.Values
                    .Select(entry => entry.Registration)
                    .OrderBy(registration => registration.Id, StringComparer.Ordinal)
            ];
        }
    }

    /// <summary>
    /// Removes the dialogs code from an unloading plugin's load context registered, and refuses its later ones.
    /// </summary>
    internal static void Release(AssemblyLoadContext owner)
    {
        lock (sync)
        {
            released.AddOrUpdate(owner, owner);

            var owned = entries
                .Where(pair => pair.Value.Owner == owner)
                .Select(pair => pair.Key)
                .ToList();

            foreach (var id in owned)
                entries.Remove(id);

            if (owned.Count > 0)
                Update();
        }
    }

    // Called under the lock once the entries changed. Registering the same dialogs again, as a reloaded plugin does,
    // gives the same fingerprint, so clients only reconfigure for dialogs that changed.
    private static void Update()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var (id, entry) in entries.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var registration = entry.Registration;
            var name = Encoding.UTF8.GetBytes(id);

            hash.AppendData(BitConverter.GetBytes(name.Length));
            hash.AppendData(name);
            hash.AppendData([(byte)(registration.QuickAction ? 1 : 0), (byte)(registration.PauseScreen ? 1 : 0)]);
            hash.AppendData(BitConverter.GetBytes(entry.Nbt.Length));
            hash.AppendData(entry.Nbt);
        }

        Fingerprint = Convert.ToHexString(hash.GetHashAndReset());
    }

    // A registration, the plugin load context that registered it, if any, and the dialog as the client gets it.
    private sealed record Entry(DialogRegistration Registration, AssemblyLoadContext? Owner, byte[] Nbt);
}

/// <summary>
/// A registered dialog.
/// </summary>
/// <param name="Id">The dialog's identifier.</param>
/// <param name="Dialog">The dialog.</param>
/// <param name="QuickAction">Whether the quick actions key opens it.</param>
/// <param name="PauseScreen">Whether the pause menu links to it.</param>
public sealed record DialogRegistration(string Id, DialogElement Dialog, bool QuickAction, bool PauseScreen);
