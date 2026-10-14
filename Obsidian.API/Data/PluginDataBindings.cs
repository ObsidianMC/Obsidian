using Obsidian.API.Plugins;
using Obsidian.Nbt;
using System.Collections.Generic;
using System.Threading;

namespace Obsidian.API.Data;

// Identifiers stay for the process; only the load's behavior goes. Attachments use identifiers on their own channel,
// so they do not extend a vanilla registry or require otherwise vanilla clients to install a mod.
internal static class PluginDataBindings
{
    private static readonly Lock gate = new();
    private static readonly Dictionary<string, PluginDataBinding?> bindings = new(StringComparer.Ordinal);
    private static int revision;

    internal static int Revision => Volatile.Read(ref revision);

    internal static void Bind(PluginDataBinding binding)
    {
        lock (gate)
        {
            if (bindings.GetValueOrDefault(binding.Key) is PluginDataBinding existing
                && existing.TryEnter() is IDisposable call)
            {
                call.Dispose();
                throw new InvalidOperationException($"{binding.Key} is already registered.");
            }

            bindings[binding.Key] = binding;
            Interlocked.Increment(ref revision);
        }
    }

    internal static bool IsCurrent(PluginDataBinding binding)
    {
        lock (gate)
            return ReferenceEquals(bindings.GetValueOrDefault(binding.Key), binding);
    }

    internal static PluginDataBinding? FindAttachment(string identifier, bool client = false)
    {
        lock (gate)
            return bindings.GetValueOrDefault((client ? "client:attachment:" : "attachment:") + identifier);
    }

    // Host-only publication under the registration gate. A codec may have been running when unload started;
    // its result must not overwrite an attachment set by the next load. Never invoke plugin code in publish.
    internal static bool TryPublish(PluginDataBinding binding, Action publish)
    {
        lock (gate)
        {
            if (!ReferenceEquals(bindings.GetValueOrDefault(binding.Key), binding))
                return false;

            using var call = binding.TryEnter();
            if (call is null)
                return false;

            publish();
            return true;
        }
    }

    internal static void Release(PluginDataBinding binding)
    {
        lock (gate)
        {
            if (!ReferenceEquals(bindings.GetValueOrDefault(binding.Key), binding))
                return;

            bindings[binding.Key] = null;
            Interlocked.Increment(ref revision);
        }
    }
}

internal sealed record PluginDataBinding(
    string Key,
    IPluginContainer Owner,
    Func<IDisposable?> TryEnter,
    bool CopyOnDeath = false,
    Action<INbtTag, INetStreamWriter>? WriteNetwork = null,
    Func<INetStreamReader, INbtTag>? ReadNetwork = null)
{
    internal IDisposable Enter()
    {
        var call = this.TryEnter();
        if (call is null)
            throw new InvalidOperationException($"{this.Key} is unloaded.");

        if (PluginDataBindings.IsCurrent(this))
            return call;

        call.Dispose();
        throw new InvalidOperationException($"{this.Key} belongs to an earlier registration.");
    }
}
