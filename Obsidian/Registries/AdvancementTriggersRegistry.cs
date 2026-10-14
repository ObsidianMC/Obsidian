using Obsidian.API.Advancements;
using Obsidian.Plugins;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>Only live registrations retain codecs; player criteria retain detached JSON.</summary>
internal static class AdvancementTriggersRegistry
{
    private static readonly Lock gate = new();
    private static readonly Dictionary<string, (PluginContainer Owner, object Registration)> registrations = [];

    public static AdvancementTrigger<T> Register<T>(
        PluginContainer owner, string identifier, AdvancementConditionsCodec<T> codec)
    {
        PluginRegistry.CheckIdentifier(owner, identifier);
        ArgumentNullException.ThrowIfNull(codec);
        var identity = new object();
        lock (gate)
        {
            if (registrations.TryGetValue(identifier, out var existing) && !ReferenceEquals(existing.Owner, owner))
                throw new ArgumentException($"Another plugin owns {identifier}.");

            registrations[identifier] = (owner, identity);
        }

        return new AdvancementTrigger<T>(identifier, async (player, matches) =>
        {
            PluginContainer active;
            IDisposable? call;
            lock (gate)
            {
                if (!registrations.TryGetValue(identifier, out var entry)
                    || !ReferenceEquals(entry.Registration, identity))
                {
                    return;
                }

                active = entry.Owner;
                call = active.TryEnter();
            }

            using (call)
            {
                if (call is not null && player is Entities.Player concrete)
                    await concrete.ProgressAdvancements.FireAsync(identifier, json => matches(codec.Decode(json)));
            }
        });
    }

    public static void ReleasePlugin(PluginContainer owner)
    {
        lock (gate)
        {
            foreach (var key in registrations.Where(entry => ReferenceEquals(entry.Value.Owner, owner))
                .Select(entry => entry.Key).ToArray())
            {
                registrations.Remove(key);
            }
        }
    }
}
