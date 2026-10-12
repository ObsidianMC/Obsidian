using Obsidian.API.Registries;
using Obsidian.API.Registry.Codecs.Biomes;
using System.Collections.Frozen;
using System.Threading;

namespace Obsidian.API.World.Features;

/// <summary>
/// A set of biomes written like vanilla's biome holder sets: biome ids and <c>#</c>-prefixed biome tags, resolved on
/// first use and again once plugins' tags change.
/// </summary>
public sealed class BiomeSet
{
    private readonly string[] entries;

    // The ids and the tag revision they were resolved at, replaced together.
    private Resolved? resolved;

    public BiomeSet(params string[] entries) => this.entries = entries;

    public IReadOnlyList<string> Entries => this.entries;

    public bool Contains(BiomeCodec biome) => this.Ids().Contains(biome.Id);

    // Plugins' biomes are registered before their tags are applied, so the tag revision covers their ids too. The
    // revision is read before resolving, so tags changing meanwhile make the next call resolve again.
    private FrozenSet<int> Ids()
    {
        var revision = TagsRegistry.Revision;
        var cached = Volatile.Read(ref this.resolved);
        if (cached is not null && cached.Revision == revision)
            return cached.Ids;

        var ids = this.Resolve();
        Volatile.Write(ref this.resolved, new Resolved(revision, ids));
        return ids;
    }

    private FrozenSet<int> Resolve()
    {
        var ids = new HashSet<int>();

        foreach (var entry in this.entries)
        {
            if (entry.StartsWith('#'))
            {
                // A tag keeps its namespace, so plugins' tags (#myplugin:hot) are found too.
                var identifier = entry[1..].Contains(':') ? entry[1..] : $"minecraft:{entry[1..]}";
                if (TagsRegistry.TryGet("worldgen/biome", identifier, out var found))
                {
                    ids.UnionWith(found.Entries);
                    continue;
                }

                // "#minecraft:has_structure/village_plains" is the tag "village_plains" of type
                // "worldgen/biome/has_structure".
                var path = entry[1..].Replace("minecraft:", string.Empty);
                var slash = path.LastIndexOf('/');
                var type = slash < 0 ? "worldgen/biome" : "worldgen/biome/" + path[..slash];
                var name = path[(slash + 1)..];
                var tag = TagsRegistry.Worldgen.Biome.All.FirstOrDefault(tag => tag.Type == type && tag.Name == name)
                    ?? throw new InvalidOperationException($"Unknown biome tag '{entry}'.");

                ids.UnionWith(tag.Entries);
            }
            else
            {
                if (!CodecRegistry.TryGetBiome(entry, out var biome))
                    throw new InvalidOperationException($"Unknown biome '{entry}'.");

                ids.Add(biome!.Id);
            }
        }

        return ids.ToFrozenSet();
    }

    private sealed record Resolved(int Revision, FrozenSet<int> Ids);
}
