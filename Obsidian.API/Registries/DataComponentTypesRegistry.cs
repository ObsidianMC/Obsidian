using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Plugins;
using Obsidian.Nbt;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Obsidian.API.Registries;

/// <summary>
/// The data component types plugins registered, after vanilla's (the generated <see cref="DataComponentType"/>): their
/// ids follow vanilla's, in registration order, and stay for the process, so a plugin's ids are values of the same
/// enum beyond vanilla's last.
/// </summary>
/// <remarks>
/// A type keeps its identifier and id when its plugin unloads; only its codecs go, so stacks hold its components as
/// they were saved (see <see cref="PluginDataComponent"/>) until the plugin registers it again.
/// </remarks>
internal static class DataComponentTypesRegistry
{
    private static readonly Lock registering = new();

    // Registering replaces the tables whole, so lookups never lock.
    private static PluginComponentTypeEntry[] types = [];
    private static FrozenDictionary<string, PluginComponentTypeEntry> byName =
        FrozenDictionary<string, PluginComponentTypeEntry>.Empty;

    /// <summary>
    /// How many component types vanilla has; ids from this one on are plugins'.
    /// </summary>
    public static int VanillaCount { get; } = (int)Enum.GetValues<DataComponentType>().Max() + 1;

    /// <summary>
    /// The component types plugins registered, in id order.
    /// </summary>
    public static IReadOnlyList<PluginComponentTypeEntry> PluginTypes => types;

    /// <summary>
    /// Whether <paramref name="type"/> is one of vanilla's.
    /// </summary>
    public static bool IsVanilla(DataComponentType type) => (uint)type < (uint)VanillaCount;

    /// <summary>
    /// Finds a plugin's component type by its id.
    /// </summary>
    public static bool TryGet(DataComponentType type, [NotNullWhen(true)] out PluginComponentTypeEntry? entry)
    {
        var current = types;
        var index = (int)type - VanillaCount;

        entry = index >= 0 && index < current.Length ? current[index] : null;
        return entry is not null;
    }

    /// <summary>
    /// Finds a plugin's component type by its identifier, ignoring case.
    /// </summary>
    public static bool TryGet(string identifier, [NotNullWhen(true)] out PluginComponentTypeEntry? entry) =>
        byName.TryGetValue(identifier, out entry);

    /// <summary>
    /// Enters a call before taking any codec from the binding. Keep the lease through the whole codec call.
    /// </summary>
    public static IDisposable? TryEnter(DataComponentType type, out PluginComponentBinding? binding)
    {
        binding = TryGet(type, out var entry) ? entry.Binding : null;
        var call = binding?.TryEnter();
        if (call is null)
            binding = null;

        return call;
    }

    /// <summary>
    /// Registers a component type, or returns the one registered before under the same identifier, which keeps its id.
    /// </summary>
    /// <exception cref="ArgumentException">The identifier is in vanilla's namespace.</exception>
    public static PluginComponentTypeEntry Register(string identifier)
    {
        if (identifier.StartsWith("minecraft:", StringComparison.Ordinal))
            throw new ArgumentException($"{identifier} is in vanilla's namespace.", nameof(identifier));

        lock (registering)
        {
            if (byName.TryGetValue(identifier, out var existing))
                return existing;

            var entry = new PluginComponentTypeEntry((DataComponentType)(VanillaCount + types.Length), identifier);

            types = [.. types, entry];
            byName = types.ToFrozenDictionary(type => type.Identifier, StringComparer.OrdinalIgnoreCase);

            return entry;
        }
    }

    /// <summary>
    /// Gives a component type its plugin's codecs, unless the plugin started unloading: then its types were released
    /// already, and binding one now would keep the plugin loaded.
    /// </summary>
    /// <returns>Whether the type was bound.</returns>
    public static bool TryBind<TValue>(
        PluginComponentTypeEntry entry,
        ComponentCodec<TValue> codec,
        ComponentNetworkCodec<TValue>? networkCodec,
        IPluginContainer plugin,
        Func<bool> closing,
        Func<IDisposable?> tryEnter)
    {
        // Network values convert from and to the saved form, which is all a stack holds.
        Func<INetStreamReader, INbtTag>? read = null;
        Action<INbtTag, INetStreamWriter>? write = null;
        Func<INbtTag, int>? hash = null;
        if (networkCodec is not null)
        {
            read = reader => codec.Encode(networkCodec.Read(reader));
            write = (tag, writer) => networkCodec.Write(writer, codec.Decode(tag));
            if (networkCodec.Hash is Func<TValue, int> hashValue)
                hash = tag => hashValue(codec.Decode(tag));
        }

        lock (registering)
        {
            if (closing())
                return false;

            // Once synced, clients need the type for as long as the server runs, like plugins' items.
            entry.IsSynced |= networkCodec is not null;
            entry.Binding = new PluginComponentBinding(plugin, tryEnter, read, write, hash);

            return true;
        }
    }

    /// <summary>
    /// Drops the codecs of every component type <paramref name="plugin"/> registered, as the plugin unloads. The types
    /// stay registered, with their ids, and stacks keep their components as saved.
    /// </summary>
    public static void ReleasePlugin(IPluginContainer plugin)
    {
        lock (registering)
        {
            foreach (var entry in types)
            {
                if (entry.Binding?.Owner == plugin)
                    entry.Binding = null;
            }
        }
    }
}

/// <summary>
/// A plugin's component type: its id and identifier, which stay, and its plugin's codecs while it's loaded.
/// </summary>
internal sealed class PluginComponentTypeEntry(DataComponentType type, string identifier)
{
    public DataComponentType Type { get; } = type;

    public string Identifier { get; } = identifier;

    /// <summary>
    /// Whether the type was ever registered with a network codec, so that clients must have it: it's in the
    /// <c>minecraft:data_component_type</c> registry modded clients are synced. Types without one are the server's
    /// own, like vanilla's components that aren't synced, and clients never hear of them.
    /// </summary>
    public bool IsSynced { get; set; }

    /// <summary>
    /// The plugin's codecs, or null while the plugin is gone.
    /// </summary>
    public PluginComponentBinding? Binding
    {
        get => Volatile.Read(ref field);
        set => Volatile.Write(ref field, value);
    }

    public override string ToString() => this.Identifier;
}

/// <summary>
/// A plugin component type's network codec, converting between the saved form stacks hold and the network's bytes;
/// both null for a type that isn't sent.
/// </summary>
internal sealed record PluginComponentBinding(
    IPluginContainer Owner,
    Func<IDisposable?> TryEnter,
    Func<INetStreamReader, INbtTag>? ReadNetwork,
    Action<INbtTag, INetStreamWriter>? WriteNetwork,
    Func<INbtTag, int>? Hash);
