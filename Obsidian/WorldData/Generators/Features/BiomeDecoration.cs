using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Generators.Mojang.Features;

namespace Obsidian.WorldData.Generators.Features;

/// <summary>
/// The 26.3 features status, beside the live generator. The caller supplies terrain-status chunks and keeps
/// their writes when decorating adjacent chunks. Structures place each step before its placed features.
/// </summary>
internal sealed class BiomeDecoration
{
    private const int VanillaStepCount = 11;
    private readonly FeatureRegistry registry;
    private readonly Dictionary<string, string[][]> biomes;
    private readonly Dictionary<(int Step, string Feature), int> indices = [];

    public BiomeDecoration(FeatureRegistry registry, IEnumerable<string> possibleBiomes)
    {
        this.registry = registry;
        var orderedBiomes = possibleBiomes.Distinct().ToArray();
        this.biomes = orderedBiomes.ToDictionary(biome => biome, registry.BiomeSteps);
        this.Steps = OrderFeatures(orderedBiomes.Select(biome => this.biomes[biome]));

        for (var step = 0; step < this.Steps.Count; step++)
        {
            for (var index = 0; index < this.Steps[step].Length; index++)
            {
                var id = this.Steps[step][index];
                this.indices.Add((step, id), index);
                registry.Placed(id);
            }
        }
    }

    public IReadOnlyList<string[]> Steps { get; }

    /// <summary>
    /// Matches ChunkGenerator.applyBiomeDecoration: all section biomes in the surrounding 3x3 chunks,
    /// restricted to this source's possible biomes, then distinct feature indices in ascending order per step.
    /// </summary>
    public IReadOnlyList<DecorationFeature> SelectFeatures(IEnumerable<IChunk> neighbors)
    {
        var present = new HashSet<string>();

        // Neighboring quarts mostly hold the same biome instance, whose name needn't be hashed again.
        BiomeCodec? last = null;

        foreach (var chunk in neighbors)
        {
            for (var y = chunk.MinY; y < chunk.MinY + chunk.Height; y += 4)
            {
                for (var z = 0; z < 16; z += 4)
                {
                    for (var x = 0; x < 16; x += 4)
                    {
                        var biome = chunk.GetBiome(x, y, z);

                        if (ReferenceEquals(biome, last))
                            continue;

                        present.Add(biome.Name);
                        last = biome;
                    }
                }
            }
        }

        var selected = new SortedSet<(int Step, int Index)>();

        foreach (var biome in present)
        {
            if (!this.biomes.TryGetValue(biome, out var steps))
                continue;

            for (var step = 0; step < steps.Length; step++)
            {
                foreach (var feature in steps[step])
                {
                    selected.Add((step, this.indices[(step, feature)]));
                }
            }
        }

        return selected.Select(item => new DecorationFeature(
            item.Step, item.Index, this.Steps[item.Step][item.Index])).ToArray();
    }

    public void Generate(
        WorldGenRegion region,
        IChunk center,
        IEnumerable<IChunk> neighbors,
        int generatorMinY,
        int generatorHeight,
        Action<int, long, WorldgenRandom>? placeStructures = null)
    {
        var chunks = neighbors.ToArray();
        var selected = this.SelectFeatures(chunks);
        var context = new DecorationContext(this.registry, region, position => region.GetBiome(position).Name,
            generatorMinY, generatorHeight);
        var origin = new Vector(center.X << 4, region.MinY, center.Z << 4);
        var random = new WorldgenRandom(new XoroshiroRandomSource(0L));
        var seed = random.SetDecorationSeed(region.Seed, origin.X, origin.Z);

        // Structure pieces run here, before features, with their own per-step registry indices. Feature seeds
        // remain independent of the number of structures (including when there are no pieces).
        for (var step = 0; step < Math.Max(VanillaStepCount, this.Steps.Count); step++)
        {
            placeStructures?.Invoke(step, seed, random);

            foreach (var feature in selected.Where(feature => feature.Step == step))
            {
                random.SetFeatureSeed(seed, feature.Index, step);
                this.registry.Placed(feature.Id).PlaceWithBiomeCheck(context, random, origin);
            }
        }

        DecorationBlockEntities.Initialize(chunks);
        center.SetChunkStatus(ChunkGenStage.features);
    }

    // FeatureSorter parity: first-encounter feature identities, step/identity graph traversal, reversed postorder.
    // Edges span step boundaries too. An ordinary alphabetical sort or a per-biome index changes decoration seeds.
    private static string[][] OrderFeatures(IEnumerable<string[][]> biomeSteps)
    {
        var identities = new Dictionary<string, int>();
        var graph = new SortedDictionary<(int Step, int Identity), SortedSet<(int Step, int Identity)>>();
        var stepCount = 0;

        foreach (var steps in biomeSteps)
        {
            stepCount = Math.Max(stepCount, steps.Length);
            var sequence = new List<(int Step, int Identity)>();

            for (var step = 0; step < steps.Length; step++)
            {
                foreach (var feature in steps[step])
                {
                    if (!identities.TryGetValue(feature, out var identity))
                    {
                        identity = identities.Count;
                        identities.Add(feature, identity);
                    }

                    sequence.Add((step, identity));
                }
            }

            for (var index = 0; index < sequence.Count; index++)
            {
                var node = sequence[index];
                if (!graph.TryGetValue(node, out var successors))
                {
                    successors = [];
                    graph.Add(node, successors);
                }

                if (index + 1 < sequence.Count)
                {
                    successors.Add(sequence[index + 1]);
                }
            }
        }

        var complete = new HashSet<(int Step, int Identity)>();
        var active = new HashSet<(int Step, int Identity)>();
        var ordered = new List<(int Step, int Identity)>();

        foreach (var node in graph.Keys)
        {
            Visit(node);
        }

        ordered.Reverse();
        var names = new string[identities.Count];

        foreach (var (name, identity) in identities)
        {
            names[identity] = name;
        }

        return Enumerable.Range(0, stepCount)
            .Select(step => ordered.Where(node => node.Step == step).Select(node => names[node.Identity]).ToArray())
            .ToArray();

        void Visit((int Step, int Identity) node)
        {
            if (complete.Contains(node))
                return;

            if (!active.Add(node))
                throw new InvalidOperationException("Biome decoration contains a feature order cycle.");

            foreach (var successor in graph[node])
            {
                Visit(successor);
            }

            active.Remove(node);
            complete.Add(node);
            ordered.Add(node);
        }
    }
}

internal sealed record DecorationFeature(int Step, int Index, string Id);
