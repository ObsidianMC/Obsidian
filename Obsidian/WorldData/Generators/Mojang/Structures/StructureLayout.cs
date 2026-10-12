using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.Plugins;
using Obsidian.WorldData.Structures;

namespace Obsidian.WorldData.Generators.Mojang.Structures;

/// <summary>
/// The structures and structure sets a dimension generates with, from a <see cref="WorldgenStructures"/>: what
/// vanilla's <c>ChunkGeneratorStructureState</c> and the structure registry give a chunk generator.
/// </summary>
internal sealed class StructureLayout
{
    /// <param name="source">The structures of the loaded plugins and data packs, or vanilla's.</param>
    /// <param name="possibleBiomes">The biomes the dimension generates.</param>
    public StructureLayout(WorldgenStructures source, IReadOnlyCollection<BiomeCodec> possibleBiomes)
    {
        this.Source = source;

        // Vanilla's hasBiomesForStructureSet: a set generates when any of its structures can start in a biome the
        // source produces.
        this.Sets = [.. source.Sets.Values
            .Where(set => set.Structures.Any(entry => possibleBiomes.Any(entry.Structure.Biomes.Contains)))
            .OrderBy(set => set.Identifier, RegistryOrder.Instance)];

        var structures = source.Structures.Values
            .OrderBy(structure => structure.Identifier, RegistryOrder.Instance)
            .ToList();

        this.StructuresPerStep = [.. Enum.GetValues<DecorationStep>()
            .Select(step => (IReadOnlyList<Structure>)[.. structures.Where(structure => structure.Step == step)])];

        this.TerrainAdapting =
            [.. structures.Where(structure => structure.TerrainAdaptation != TerrainAdjustment.None)];
    }

    public WorldgenStructures Source { get; }

    /// <summary>
    /// Later for layouts of later structure sets.
    /// </summary>
    public long Version => this.Source.Version;

    /// <summary>
    /// The structure sets that may start in the dimension, in registry order.
    /// </summary>
    public IReadOnlyList<StructureSet> Sets { get; }

    /// <summary>
    /// Every structure of each decoration step in registry order, which decides their feature seed index.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<Structure>> StructuresPerStep { get; }

    /// <summary>
    /// The structures the beardifier reshapes terrain for, in the order it goes through them.
    /// </summary>
    public IReadOnlyList<Structure> TerrainAdapting { get; }

    /// <summary>
    /// The plugins whose code a structure runs while it starts and is placed; none for most.
    /// </summary>
    public ImmutableArray<PluginContainer> OwnersOf(Structure structure) =>
        this.Source.Owners.GetValueOrDefault(structure, []);

    /// <summary>
    /// Vanilla's order of a data pack registry's entries: it lists their files sorted by identifier, path first,
    /// then namespace. Must match, since a structure's place in its step seeds its decoration.
    /// </summary>
    private sealed class RegistryOrder : IComparer<string>
    {
        public static RegistryOrder Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            var (xNamespace, xPath) = Split(x!);
            var (yNamespace, yPath) = Split(y!);

            // The files' paths end with .json, which comes before a longer path continuing with '-'.
            var byPath = string.CompareOrdinal($"{xPath}.json", $"{yPath}.json");
            return byPath != 0 ? byPath : string.CompareOrdinal(xNamespace, yNamespace);
        }

        private static (string Namespace, string Path) Split(string identifier)
        {
            var colon = identifier.IndexOf(':');
            return colon < 0 ? ("minecraft", identifier) : (identifier[..colon], identifier[(colon + 1)..]);
        }
    }
}
