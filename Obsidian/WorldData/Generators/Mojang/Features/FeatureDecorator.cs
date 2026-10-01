using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Generators.Mojang.Features;

/// <summary>
/// The features step of vanilla's ChunkGenerator (applyBiomeDecoration): places every biome feature of a chunk.
/// </summary>
/// <remarks>
/// Features come from the biomes of the 3x3 chunks around the chunk being decorated, ordered by
/// <see cref="FeatureSorter"/>. Each feature gets its own seed from the chunk's decoration seed, its index in the
/// step and the step. Structures aren't generated yet, so their part of each step is skipped.
/// </remarks>
internal sealed class FeatureDecorator
{
    // Vanilla's GenerationStep.Decoration values.
    private const int DecorationStepCount = 11;

    private readonly IReadOnlyList<StepFeatures> featuresPerStep;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<PlacedFeature>>> biomeFeatures;
    private readonly IReadOnlyDictionary<string, HashSet<PlacedFeature>> biomeFeatureSets;
    private readonly HashSet<string> possibleBiomes;

    /// <param name="possibleBiomes">Biomes the biome source can produce, in vanilla's possibleBiomes order.</param>
    /// <param name="biomeFeatures">Decoration steps of each biome, keyed by biome id.</param>
    public FeatureDecorator(IReadOnlyList<BiomeCodec> possibleBiomes, IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<PlacedFeature>>> biomeFeatures)
    {
        this.biomeFeatures = biomeFeatures;
        this.possibleBiomes = possibleBiomes.Select(biome => biome.Name).ToHashSet();
        this.featuresPerStep = FeatureSorter.Build(possibleBiomes, biome => this.GetSteps(biome.Name));
        this.biomeFeatureSets = biomeFeatures.ToDictionary(entry => entry.Key,
            entry => entry.Value.SelectMany(step => step).ToHashSet());
    }

    /// <summary>
    /// Decorates the chunk at (<paramref name="chunkX"/>, <paramref name="chunkZ"/>) through <paramref name="region"/>,
    /// which must hold that chunk and its 8 neighbors.
    /// </summary>
    /// <param name="areaChunks">The 3x3 chunks around the chunk; their biomes decide which features run.</param>
    /// <param name="onPlaced">Optional callback with the step, global index, feature and whether it placed anything.</param>
    public void Decorate(WorldGenRegion region, IReadOnlyCollection<IChunk> areaChunks, int chunkX, int chunkZ,
        Action<int, int, PlacedFeature, bool>? onPlaced = null)
    {
        var origin = new Vector(chunkX << 4, region.MinY, chunkZ << 4);
        var generation = new WorldGenerationContext(region.MinY, region.Height);

        // The initial seed doesn't matter: the decoration seed replaces it.
        var random = new WorldgenRandom(new XoroshiroRandomSource(0L));
        var decorationSeed = random.SetDecorationSeed(region.Seed, origin.X, origin.Z);

        var biomes = this.CollectBiomes(areaChunks, region.MinY, region.Height);
        var stepCount = Math.Max(DecorationStepCount, this.featuresPerStep.Count);

        for (var step = 0; step < stepCount; step++)
        {
            if (step >= this.featuresPerStep.Count)
                continue;

            var stepFeatures = this.featuresPerStep[step];
            var indices = new SortedSet<int>();

            foreach (var biome in biomes)
            {
                var steps = this.GetSteps(biome);
                if (step < steps.Count)
                {
                    foreach (var feature in steps[step])
                        indices.Add(stepFeatures.IndexOf(feature));
                }
            }

            foreach (var index in indices)
            {
                var feature = stepFeatures.Features[index];
                random.SetFeatureSeed(decorationSeed, index, step);

                try
                {
                    var placed = feature.PlaceWithBiomeCheck(region, generation, random, origin, this.BiomeHasFeature);
                    onPlaced?.Invoke(step, index, feature, placed);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"Failed to place feature '{feature.Identifier}' in chunk ({chunkX}, {chunkZ}) (decoration seed {decorationSeed}).", exception);
                }
            }
        }
    }

    private bool BiomeHasFeature(BiomeCodec biome, PlacedFeature feature) =>
        this.biomeFeatureSets.TryGetValue(biome.Name, out var features) && features.Contains(feature);

    private IReadOnlyList<IReadOnlyList<PlacedFeature>> GetSteps(string biome) =>
        this.biomeFeatures.TryGetValue(biome, out var steps) ? steps : [];

    /// <summary>
    /// Every biome stored in the area's chunks that the biome source can produce.
    /// </summary>
    private HashSet<string> CollectBiomes(IReadOnlyCollection<IChunk> chunks, int minY, int height)
    {
        var biomes = new HashSet<string>();

        foreach (var chunk in chunks)
        {
            for (var quartY = minY >> 2; quartY < (minY + height) >> 2; quartY++)
            {
                for (var quartZ = 0; quartZ < 4; quartZ++)
                {
                    for (var quartX = 0; quartX < 4; quartX++)
                        biomes.Add(chunk.GetBiome(quartX << 2, quartY << 2, quartZ << 2).Name);
                }
            }
        }

        biomes.IntersectWith(this.possibleBiomes);
        return biomes;
    }
}
