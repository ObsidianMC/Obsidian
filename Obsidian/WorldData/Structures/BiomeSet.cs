using Obsidian.API.Registry.Codecs.Biomes;
using System.Collections.Frozen;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// A set of biomes written like vanilla's biome holder sets: biome ids and <c>#</c>-prefixed biome tags, resolved on
/// first use.
/// </summary>
public sealed class BiomeSet
{
    private readonly string[] entries;

    public BiomeSet(params string[] entries) => this.entries = entries;

    public IReadOnlyList<string> Entries => this.entries;

    private FrozenSet<int> Ids => field ??= this.Resolve();

    public bool Contains(BiomeCodec biome) => this.Ids.Contains(biome.Id);

    private FrozenSet<int> Resolve()
    {
        var ids = new HashSet<int>();

        foreach (var entry in this.entries)
        {
            if (entry.StartsWith('#'))
            {
                // "#minecraft:has_structure/village_plains" is the tag "village_plains" of type "worldgen/biome/has_structure".
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
}
