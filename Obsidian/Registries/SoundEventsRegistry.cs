using Obsidian.API.Plugins;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>
/// Active plugin sound declarations. Identity is the identifier: sound packets use direct holders, so there are no
/// process-local numeric ids to sync. Entries are released on unload and re-created under the same names on reload.
/// </summary>
internal static class SoundEventsRegistry
{
    private static readonly Lock gate = new();
    private static readonly Dictionary<string, (SoundEvent Event, IPluginContainer Owner)> events = [];

    internal static SoundEvent Register(Plugins.PluginContainer owner, string identifier, float? fixedRange)
    {
        if (fixedRange is float range && (!float.IsFinite(range) || range < 0))
            throw new ArgumentOutOfRangeException(nameof(fixedRange));

        lock (gate)
        {
            if (owner.IsClosing)
                throw new InvalidOperationException($"{owner.Info.Name} is unloading.");

            var sound = new SoundEvent
            {
                ResourceLocation = identifier,
                FixedRange = fixedRange
            };
            events[identifier] = (sound, owner);
            return sound;
        }
    }

    internal static bool TryGet(string identifier, out SoundEvent sound)
    {
        lock (gate)
        {
            var found = events.TryGetValue(identifier, out var entry);
            sound = entry.Event;
            return found;
        }
    }

    internal static void ReleasePlugin(IPluginContainer owner)
    {
        lock (gate)
        {
            var owned = events.Where(entry => entry.Value.Owner == owner)
                .Select(entry => entry.Key)
                .ToArray();

            foreach (var identifier in owned)
                events.Remove(identifier);
        }
    }
}
