using Obsidian.API.Plugins;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>Custom stat identities remain stable; unload removes their owner and ability to increment them.</summary>
internal static class CustomStatisticsRegistry
{
    private static readonly Lock gate = new();
    private static readonly List<(string Identifier, IPluginContainer? Owner)> entries = [];

    public static IReadOnlyList<string> Vanilla => VanillaRegistryIds.Get("minecraft:custom_stat");

    public static string[] Plugins
    {
        get
        {
            lock (gate)
                return entries.Select(entry => entry.Identifier).ToArray();
        }
    }

    public static int Register(Plugins.PluginContainer owner, string identifier)
    {
        Obsidian.Plugins.PluginRegistry.CheckIdentifier(owner, identifier);
        lock (gate)
        {
            var index = entries.FindIndex(entry => entry.Identifier == identifier);
            if (index < 0)
            {
                index = entries.Count;
                entries.Add((identifier, owner));
            }
            else
            {
                if (entries[index].Owner is not null && !ReferenceEquals(entries[index].Owner, owner))
                    throw new InvalidOperationException($"Statistic {identifier} already has an owner.");

                entries[index] = (identifier, owner);
            }

            return Vanilla.Count + index;
        }
    }

    public static int GetId(string identifier, bool requireActive = false)
    {
        for (var id = 0; id < Vanilla.Count; id++)
        {
            if (Vanilla[id] == identifier)
                return id;
        }

        lock (gate)
        {
            var index = entries.FindIndex(entry => entry.Identifier == identifier);
            return index < 0 || requireActive && entries[index].Owner is null ? -1 : Vanilla.Count + index;
        }
    }

    public static void ReleasePlugin(IPluginContainer owner)
    {
        lock (gate)
        {
            for (var index = 0; index < entries.Count; index++)
            {
                if (ReferenceEquals(entries[index].Owner, owner))
                    entries[index] = (entries[index].Identifier, null);
            }
        }
    }
}
