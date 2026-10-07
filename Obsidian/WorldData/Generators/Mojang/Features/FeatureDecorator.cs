using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Generators.Mojang.Structures;
using Obsidian.WorldData.Structures;
using BitOperations = System.Numerics.BitOperations;

namespace Obsidian.WorldData.Generators.Mojang.Features;

/// <summary>
/// The features step of vanilla's ChunkGenerator (applyBiomeDecoration): places every biome feature of a chunk.
/// </summary>
/// <remarks>
/// Features come from the biomes of the 3x3 chunks around the chunk being decorated, ordered by
/// <see cref="FeatureSorter"/>. Each step first places the structures reaching the chunk, then the features; each structure
/// and feature gets its own seed from the chunk's decoration seed, its index in the step and the step.
/// </remarks>
internal sealed class FeatureDecorator
{
    // Vanilla's GenerationStep.Decoration values.
    private const int DecorationStepCount = 11;

    private readonly IReadOnlyList<StepFeatures> featuresPerStep;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<PlacedFeature>>> biomeFeatures;
    private readonly Dictionary<string, HashSet<PlacedFeature>> biomeFeatureSets;

    // The feature sets of the possible biomes by biome id, with each biome's name to check lookups against: biome filters
    // look them up for nearly every position placed.
    private readonly (string? Name, HashSet<PlacedFeature>? Features)[] featureSetsById;
    private readonly HashSet<string> possibleBiomes;

    // For each possible biome, the indices of its features in each step's sorted features.
    private readonly Dictionary<string, int[][]> stepIndices = [];

    private readonly Func<BiomeCodec, PlacedFeature, bool> biomeHasFeature;
    private readonly int maxStepWords;
    private readonly int generationDepth;

    /// <param name="possibleBiomes">Biomes the biome source can produce, in vanilla's possibleBiomes order.</param>
    /// <param name="biomeFeatures">Decoration steps of each biome, keyed by biome id.</param>
    /// <param name="generationDepth">The noise settings' height, which caps the height placements see.</param>
    public FeatureDecorator(IReadOnlyList<BiomeCodec> possibleBiomes,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<PlacedFeature>>> biomeFeatures, int generationDepth)
    {
        this.biomeFeatures = biomeFeatures;
        this.generationDepth = generationDepth;
        this.possibleBiomes = possibleBiomes.Select(biome => biome.Name).ToHashSet();
        this.featuresPerStep = FeatureSorter.Build(possibleBiomes, biome => this.GetSteps(biome.Name));
        this.biomeFeatureSets = biomeFeatures.ToDictionary(entry => entry.Key,
            entry => entry.Value.SelectMany(step => step).ToHashSet());
        this.biomeHasFeature = this.BiomeHasFeature;
        this.maxStepWords = this.featuresPerStep.Count == 0 ? 0 : (this.featuresPerStep.Max(step => step.Features.Count) + 63) >> 6;

        foreach (var biome in this.possibleBiomes)
        {
            var steps = this.GetSteps(biome);
            this.stepIndices[biome] = [.. steps.Take(this.featuresPerStep.Count)
                .Select((features, step) => features.Select(this.featuresPerStep[step].IndexOf).ToArray())];
        }

        this.featureSetsById = new (string?, HashSet<PlacedFeature>?)[possibleBiomes.Count == 0 ? 0 : possibleBiomes.Max(biome => biome.Id) + 1];
        foreach (var biome in possibleBiomes)
        {
            if (biome.Id >= 0)
                this.featureSetsById[biome.Id] = (biome.Name, this.biomeFeatureSets.GetValueOrDefault(biome.Name));
        }
    }

    /// <summary>
    /// Decorates the chunk at (<paramref name="chunkX"/>, <paramref name="chunkZ"/>) through <paramref name="region"/>,
    /// which must hold that chunk and its 8 neighbors.
    /// </summary>
    /// <param name="areaChunks">The 3x3 chunks around the chunk; their biomes decide which features run.</param>
    /// <param name="structures">The world's structures, or <c>null</c> when structures aren't generated.</param>
    /// <param name="terrain">The noise terrain, for structure pieces that read it.</param>
    /// <param name="onPlaced">Optional callback with the step, global index, feature and whether it placed anything.</param>
    public void Decorate(WorldGenRegion region, IReadOnlyCollection<IChunk> areaChunks, int chunkX, int chunkZ,
        StructureManager? structures, IStructureTerrain terrain, Action<int, int, PlacedFeature, bool>? onPlaced = null)
    {
        var origin = new Vector(chunkX << 4, region.MinY, chunkZ << 4);
        // Like vanilla's PlacementContext: the level's min Y, but at most the generator's depth (128 in the nether), so
        // anchors such as "below top" resolve against the noise range.
        var generation = new WorldGenerationContext(region.MinY, Math.Min(region.Height, this.generationDepth));

        // The initial seed doesn't matter: the decoration seed replaces it.
        var random = new WorldgenRandom(new XoroshiroRandomSource(0L));
        var decorationSeed = random.SetDecorationSeed(region.Seed, origin.X, origin.Z);

        var biomes = this.CollectBiomes(areaChunks, region.MinY, region.Height);
        var stepCount = Math.Max(DecorationStepCount, this.featuresPerStep.Count);

        // Vanilla's getWritableArea: the chunk, above the bottom layer.
        var writableArea = BlockBox.Create(origin.X, region.MinY + 1, origin.Z, origin.X + 15, region.MinY + region.Height - 1, origin.Z + 15);

        // Few structures reach a chunk, and looking for the starts of one structure scans as many chunks as looking for
        // all of them; the others have nothing to place.
        var reachingStructures = structures?.GetStartsReaching(chunkX, chunkZ).Select(start => start.Structure)
            .ToHashSet(ReferenceEqualityComparer.Instance) ?? [];

        // The features of the area's biomes in a step, one bit per index in the step's features.
        Span<ulong> chosen = stackalloc ulong[this.maxStepWords];

        for (var step = 0; step < stepCount; step++)
        {
            if (structures is not null && step < structures.StructuresPerStep.Count)
            {
                var stepStructures = structures.StructuresPerStep[step];
                for (var index = 0; index < stepStructures.Count; index++)
                {
                    if (!reachingStructures.Contains(stepStructures[index]))
                        continue;

                    random.SetFeatureSeed(decorationSeed, index, step);
                    var context = new StructurePieceContext(region, random, writableArea, chunkX, chunkZ, default)
                    {
                        Terrain = terrain,
                        Generation = generation
                    };

                    foreach (var start in structures.GetReferencingStarts(chunkX, chunkZ, stepStructures[index]))
                    {
                        try
                        {
                            start.PlaceInChunk(context);
                        }
                        catch (Exception exception)
                        {
                            throw new InvalidOperationException(
                                $"Failed to place structure '{start.Structure.Identifier}' in chunk ({chunkX}, {chunkZ}).", exception);
                        }
                    }
                }
            }

            if (step >= this.featuresPerStep.Count)
                continue;

            var stepFeatures = this.featuresPerStep[step];
            chosen.Clear();
            foreach (var biome in biomes)
            {
                var steps = this.stepIndices[biome];
                if (step < steps.Length)
                {
                    foreach (var index in steps[step])
                        chosen[index >> 6] |= 1UL << index;
                }
            }

            // Placed in index order.
            for (var word = 0; word < chosen.Length; word++)
            {
                for (var bits = chosen[word]; bits != 0; bits &= bits - 1)
                {
                    var index = (word << 6) + BitOperations.TrailingZeroCount(bits);
                    var feature = stepFeatures.Features[index];
                    random.SetFeatureSeed(decorationSeed, index, step);

                    try
                    {
                        var placed = feature.PlaceWithBiomeCheck(region, generation, random, origin, this.biomeHasFeature);
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
    }

    private bool BiomeHasFeature(BiomeCodec biome, PlacedFeature feature)
    {
        var id = biome.Id;
        if ((uint)id < (uint)this.featureSetsById.Length)
        {
            var (name, features) = this.featureSetsById[id];
            if (name == biome.Name)
                return features is not null && features.Contains(feature);
        }

        return this.biomeFeatureSets.TryGetValue(biome.Name, out var set) && set.Contains(feature);
    }

    private IReadOnlyList<IReadOnlyList<PlacedFeature>> GetSteps(string biome) =>
        this.biomeFeatures.TryGetValue(biome, out var steps) ? steps : [];

    /// <summary>
    /// Every biome stored in the area's chunks that the biome source can produce.
    /// </summary>
    private HashSet<string> CollectBiomes(IReadOnlyCollection<IChunk> chunks, int minY, int height)
    {
        var biomes = new HashSet<string>();

        BiomeCodec? previous = null;

        foreach (var chunk in chunks)
        {
            // A section that holds a single biome has it in every cell.
            if (chunk.Sections.Length == height >> 4 && chunk.MinY == minY)
            {
                foreach (var section in chunk.Sections)
                {
                    if (section.BiomeContainer.Palette is SingleValuePalette<BiomeCodec> { IsFull: true } single)
                        biomes.Add(single.Value.Name);
                    else
                        AddSectionBiomes(biomes, section, ref previous);
                }

                continue;
            }

            for (var quartY = minY >> 2; quartY < (minY + height) >> 2; quartY++)
            {
                for (var quartZ = 0; quartZ < 4; quartZ++)
                {
                    for (var quartX = 0; quartX < 4; quartX++)
                    {
                        // Neighboring cells mostly share their biome, which is then already in the set.
                        var biome = chunk.GetBiome(quartX << 2, quartY << 2, quartZ << 2);
                        if (!ReferenceEquals(biome, previous))
                            biomes.Add(biome.Name);

                        previous = biome;
                    }
                }
            }
        }

        biomes.IntersectWith(this.possibleBiomes);
        return biomes;
    }

    private static void AddSectionBiomes(HashSet<string> biomes, IChunkSection section, ref BiomeCodec? previous)
    {
        for (var y = 0; y < 4; y++)
        {
            for (var z = 0; z < 4; z++)
            {
                for (var x = 0; x < 4; x++)
                {
                    // Neighboring cells mostly share their biome, which is then already in the set.
                    var biome = section.GetBiome(x, y, z);
                    if (!ReferenceEquals(biome, previous))
                        biomes.Add(biome.Name);

                    previous = biome;
                }
            }
        }
    }
}
