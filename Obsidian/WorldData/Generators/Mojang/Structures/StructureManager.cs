using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Nbt;
using Obsidian.WorldData.Structures;
using Obsidian.WorldData.Structures.Placement;

namespace Obsidian.WorldData.Generators.Mojang.Structures;

/// <summary>
/// Structure starts and references of a world, like vanilla's <c>ChunkGeneratorStructureState</c>, the structure parts of
/// <c>ChunkGenerator</c> (<c>createStructures</c>, <c>createReferences</c>) and <c>StructureManager</c>.
/// </summary>
/// <remarks>
/// <para>
/// Starts only depend on the seed, the biome source and the noise, so they're computed on demand for any chunk and cached
/// by chunk position instead of being stored in chunks (vanilla generates the chunks around a chunk to the structure
/// starts status). Thread-safe.
/// </para>
/// <para>
/// What pieces change as they're placed (a chest or spawner placed, the height a temple settled at) is saved in the start
/// chunk like vanilla's <c>structures.starts</c> (see <see cref="SaveStarts"/>), and restored into the recomputed start
/// before it's placed again (see <see cref="StructureStart.RestoreState"/>), so a restart places the rest of a structure
/// the same way.
/// </para>
/// </remarks>
internal sealed class StructureManager : IStructurePlacementState
{
    // Vanilla's createReferences radius: starts within 8 chunks can reach a chunk.
    private const int ReferenceRadius = 8;

    private readonly RandomState randomState;
    private readonly IClimateBiomeSource biomeSource;
    private readonly IStructureTerrain terrain;
    private readonly int minY;
    private readonly int height;
    private readonly IReadOnlyList<StructureSet> structureSets;
    private readonly ConcurrentDictionary<(int X, int Z), StructureStart[]> starts = new();
    private readonly ConcurrentDictionary<ConcentricRingsStructurePlacement, Lazy<IReadOnlySet<(int X, int Z)>>> ringPositions = new();

    // The structures the beardifier reshapes terrain for, in the order it goes through them.
    private readonly Structure[] terrainAdaptingStructures;

    // See GetReachingStarts.
    [ThreadStatic]
    private static ReachingStarts? lastReachingStarts;

    public long Seed { get; }

    /// <summary>
    /// The structure sets of the dimension, in registry order.
    /// </summary>
    public IReadOnlyList<StructureSet> Sets => this.structureSets;

    /// <summary>
    /// The structures of each decoration step in registry order, which decides their feature seed index.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<Structure>> StructuresPerStep { get; }

    public StructureManager(RandomState randomState, IClimateBiomeSource biomeSource, IStructureTerrain terrain, int minY, int height)
    {
        this.randomState = randomState;
        this.biomeSource = biomeSource;
        this.terrain = terrain;
        this.minY = minY;
        this.height = height;
        this.Seed = randomState.Seed;

        // Vanilla's registries are loaded from the data pack, so they're in id order.
        this.structureSets = [.. StructureSets.All.Values
            .Where(this.HasBiomesFor)
            .OrderBy(set => set.Identifier, StringComparer.Ordinal)];

        var structures = Obsidian.Registries.Structures.All.Values.OrderBy(structure => structure.Identifier, StringComparer.Ordinal).ToList();
        this.StructuresPerStep = [.. Enum.GetValues<DecorationStep>()
            .Select(step => (IReadOnlyList<Structure>)[.. structures.Where(structure => structure.Step == step)])];
        this.terrainAdaptingStructures = [.. this.StructuresPerStep.SelectMany(step => step)
            .OrderBy(structure => structure.Identifier, StringComparer.Ordinal)
            .Where(structure => structure.TerrainAdaptation != TerrainAdjustment.None)];
    }

    /// <summary>
    /// The structures that start in a chunk (vanilla's structure starts status), at most one per structure set.
    /// </summary>
    public StructureStart[] GetStarts(int chunkX, int chunkZ) => this.starts.GetOrAdd((chunkX, chunkZ), key => this.CreateStarts(key.X, key.Z));

    /// <summary>
    /// The starts of <paramref name="structure"/> whose box reaches the chunk, in the order vanilla places them.
    /// </summary>
    public List<StructureStart> GetReferencingStarts(int chunkX, int chunkZ, Structure structure)
    {
        // Vanilla adds the reference chunks to a LongOpenHashSet in this order and places the starts in the set's order.
        List<long>? references = null;
        Dictionary<long, StructureStart>? byChunk = null;

        foreach (var (key, start) in this.GetReachingStarts(chunkX, chunkZ))
        {
            if (start.Structure != structure)
                continue;

            (references ??= []).Add(key);
            (byChunk ??= [])[key] = start;
        }

        return references is null ? [] : [.. LongHashSetOrder.Order(references).Select(key => byChunk![key])];
    }

    /// <summary>
    /// The starts whose box reaches the chunk with their start chunk's key, start chunk by start chunk (X, then Z).
    /// </summary>
    /// <remarks>
    /// Decorating a chunk asks for the starts of every structure in turn, so each thread keeps the last chunk's starts.
    /// Starts don't change once created.
    /// </remarks>
    private List<(long Key, StructureStart Start)> GetReachingStarts(int chunkX, int chunkZ)
    {
        var last = lastReachingStarts;
        if (last is not null && last.Owner == this && last.ChunkX == chunkX && last.ChunkZ == chunkZ)
            return last.Starts;

        var minX = chunkX << 4;
        var minZ = chunkZ << 4;
        var reaching = new List<(long, StructureStart)>();

        for (var x = chunkX - ReferenceRadius; x <= chunkX + ReferenceRadius; x++)
        {
            for (var z = chunkZ - ReferenceRadius; z <= chunkZ + ReferenceRadius; z++)
            {
                foreach (var start in this.GetStarts(x, z))
                {
                    if (start.BoundingBox.Intersects(minX, minZ, minX + 15, minZ + 15))
                        reaching.Add((ChunkKey(x, z), start));
                }
            }
        }

        lastReachingStarts = new ReachingStarts(this, chunkX, chunkZ, reaching);
        return reaching;
    }

    /// <summary>
    /// Every start whose box reaches the chunk, of any structure: the starts placed when the chunk is decorated.
    /// </summary>
    public IEnumerable<StructureStart> GetStartsReaching(int chunkX, int chunkZ)
    {
        var minX = chunkX << 4;
        var minZ = chunkZ << 4;

        for (var x = chunkX - ReferenceRadius; x <= chunkX + ReferenceRadius; x++)
        {
            for (var z = chunkZ - ReferenceRadius; z <= chunkZ + ReferenceRadius; z++)
            {
                foreach (var start in this.GetStarts(x, z))
                {
                    if (start.BoundingBox.Intersects(minX, minZ, minX + 15, minZ + 15))
                        yield return start;
                }
            }
        }
    }

    /// <summary>
    /// Loads a start chunk, waiting for it, and returns the structure starts saved in it (or <c>null</c>); set by the
    /// generator. Without it, starts are restored without saved state.
    /// </summary>
    internal Func<int, int, NbtCompound?>? LoadStartChunk { get; set; }

    /// <summary>
    /// Loads a start's start chunk, restoring the start's saved state the first time, for code that changes the start
    /// outside of chunk decoration (structure searches taking references). Keeping the chunk loaded saves the change.
    /// </summary>
    /// <remarks>
    /// Like vanilla's structure searches, which load start chunks on the server thread, this waits for the chunk.
    /// </remarks>
    public void LoadStart(StructureStart start)
    {
        var saved = this.LoadStartChunk?.Invoke(start.ChunkX, start.ChunkZ);
        RestoreStart(start, saved);
    }

    /// <summary>
    /// Restores a start from the structure starts saved in its start chunk (vanilla's <c>structures.starts</c>), unless it
    /// was restored already.
    /// </summary>
    public static void RestoreStart(StructureStart start, NbtCompound? savedStarts) =>
        start.RestoreState(savedStarts is not null && savedStarts.TryGetTag<NbtCompound>(start.Structure.Identifier, out var saved) ? saved : null);

    /// <summary>
    /// The structure starts to save in their start chunk (vanilla's <c>structures.starts</c>, keyed by structure id): those
    /// the chunk was loaded with, where starts restored in this session replace theirs with their pieces' current state.
    /// </summary>
    /// <remarks>
    /// A start that wasn't restored hasn't been placed in this session, so what the chunk was loaded with is still current.
    /// </remarks>
    public NbtCompound? SaveStarts(int chunkX, int chunkZ, NbtCompound? loaded)
    {
        if (!this.starts.TryGetValue((chunkX, chunkZ), out var cached) || !cached.Any(start => start.IsStateRestored))
            return loaded;

        var result = new NbtCompound("starts");
        if (loaded is not null)
        {
            foreach (var (name, start) in loaded)
                result.Add(name, start);
        }

        foreach (var start in cached)
        {
            if (!start.IsStateRestored)
                continue;

            result.Remove(start.Structure.Identifier);
            result.Add(start.SaveState());
        }

        return result;
    }

    /// <summary>
    /// Every start of a structure with terrain adaptation whose box reaches the chunk, for the beardifier.
    /// </summary>
    /// <remarks>
    /// Vanilla goes through the structures in hash map order, which isn't stable between runs; registry order is used here.
    /// </remarks>
    public IEnumerable<StructureStart> GetTerrainAdaptingStarts(int chunkX, int chunkZ)
    {
        foreach (var structure in this.terrainAdaptingStructures)
        {
            foreach (var start in this.GetReferencingStarts(chunkX, chunkZ, structure))
                yield return start;
        }
    }

    public IReadOnlySet<(int X, int Z)> GetRingPositions(ConcentricRingsStructurePlacement placement) =>
        this.ringPositions.GetOrAdd(placement, key => new Lazy<IReadOnlySet<(int X, int Z)>>(() => this.CreateRingPositions(key))).Value;

    public bool HasStructureChunkInRange(StructureSet set, int chunkX, int chunkZ, int range)
    {
        for (var x = chunkX - range; x <= chunkX + range; x++)
        {
            for (var z = chunkZ - range; z <= chunkZ + range; z++)
            {
                if (set.Placement.IsStructureChunk(this, x, z))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Vanilla <c>ChunkGenerator.createStructures</c> for one chunk.
    /// </summary>
    private StructureStart[] CreateStarts(int chunkX, int chunkZ)
    {
        List<StructureStart>? result = null;

        foreach (var set in this.structureSets)
        {
            if (!set.Placement.IsStructureChunk(this, chunkX, chunkZ))
                continue;

            var start = set.Structures.Length == 1
                ? this.TryGenerate(set.Structures[0].Structure, chunkX, chunkZ)
                : this.TryGenerateWeighted(set.Structures, chunkX, chunkZ);

            if (start is not null)
                (result ??= []).Add(start);
        }

        return result is null ? [] : [.. result];
    }

    /// <summary>
    /// Picks the set's structures by weight until one starts, dropping each that doesn't.
    /// </summary>
    private StructureStart? TryGenerateWeighted(StructureSetEntry[] entries, int chunkX, int chunkZ)
    {
        var candidates = entries.ToList();
        var random = new WorldgenRandom(new LegacyRandomSource(0L));
        random.SetLargeFeatureSeed(this.Seed, chunkX, chunkZ);
        var totalWeight = candidates.Sum(entry => entry.Weight);

        while (candidates.Count > 0)
        {
            var pick = random.NextInt(totalWeight);
            var index = 0;
            foreach (var entry in candidates)
            {
                pick -= entry.Weight;
                if (pick < 0)
                    break;

                index++;
            }

            var chosen = candidates[index];
            var start = this.TryGenerate(chosen.Structure, chunkX, chunkZ);
            if (start is not null)
                return start;

            candidates.RemoveAt(index);
            totalWeight -= chosen.Weight;
        }

        return null;
    }

    private StructureStart? TryGenerate(Structure structure, int chunkX, int chunkZ)
    {
        var random = new WorldgenRandom(new LegacyRandomSource(0L));
        random.SetLargeFeatureSeed(this.Seed, chunkX, chunkZ);

        return structure.Generate(new StructureGenerationContext
        {
            ChunkX = chunkX,
            ChunkZ = chunkZ,
            Seed = this.Seed,
            Random = random,
            Terrain = this.terrain,
            BiomeSource = this.biomeSource,
            Sampler = this.randomState.ClimateSampler,
            ValidBiome = structure.Biomes.Contains,
            MinY = this.minY,
            Height = this.height,
            Generation = new WorldGenerationContext(Math.Max(this.minY, this.randomState.Settings.Noise.MinY),
                Math.Min(this.height, this.randomState.Settings.Noise.Height))
        });
    }

    /// <summary>
    /// Vanilla <c>hasBiomesForStructureSet</c>: whether any structure of the set can start in a biome the source produces.
    /// </summary>
    private bool HasBiomesFor(StructureSet set) =>
        set.Structures.Any(entry => this.biomeSource.PossibleBiomes.Any(entry.Structure.Biomes.Contains));

    /// <summary>
    /// Vanilla <c>ChunkGeneratorStructureState.generateRingPositions</c>: starts spread over rings, each moved to a preferred
    /// biome found within 112 blocks.
    /// </summary>
    private IReadOnlySet<(int X, int Z)> CreateRingPositions(ConcentricRingsStructurePlacement placement)
    {
        if (placement.Count == 0)
            return new HashSet<(int X, int Z)>();

        var random = new LegacyRandomSource(this.Seed);
        var angle = random.NextDouble() * Math.PI * 2.0;
        var distance = placement.Distance;
        var spread = placement.Spread;
        var placedOnRing = 0;
        var ring = 0;
        var candidates = new List<(int X, int Z, IRandomSource Random)>(placement.Count);

        for (var index = 0; index < placement.Count; index++)
        {
            var radius = 4 * distance + distance * ring * 6 + (random.NextDouble() - 0.5) * (distance * 2.5);
            // Java's Math.round rounds halves up.
            var chunkX = (int)Math.Floor(Math.Cos(angle) * radius + 0.5);
            var chunkZ = (int)Math.Floor(Math.Sin(angle) * radius + 0.5);
            candidates.Add((chunkX, chunkZ, random.Fork()));

            angle += Math.PI * 2 / spread;
            if (++placedOnRing == spread)
            {
                ring++;
                placedOnRing = 0;
                spread += 2 * spread / (ring + 1);
                spread = Math.Min(spread, placement.Count - index);
                angle += random.NextDouble() * Math.PI * 2.0;
            }
        }

        // Each search has its own forked random, so they can run in parallel.
        var positions = new (int X, int Z)[candidates.Count];
        Parallel.For(0, candidates.Count, index =>
        {
            var (chunkX, chunkZ, searchRandom) = candidates[index];
            var found = this.FindBiomeHorizontal((chunkX << 4) + 8, 0, (chunkZ << 4) + 8, 112, placement.PreferredBiomes, searchRandom);
            positions[index] = found is null ? (chunkX, chunkZ) : (found.Value.X >> 4, found.Value.Z >> 4);
        });

        return positions.ToHashSet();
    }

    /// <summary>
    /// Vanilla <c>BiomeSource.findBiomeHorizontal</c> (step 1, not closest first): a random matching quart on the outer ring
    /// of the square, picked by reservoir sampling.
    /// </summary>
    private Vector? FindBiomeHorizontal(int x, int y, int z, int radius, BiomeSet biomes, IRandomSource random)
    {
        var sampler = this.randomState.ClimateSampler;
        var quartX = x >> 2;
        var quartZ = z >> 2;
        var quartY = y >> 2;
        var quartRadius = radius >> 2;
        Vector? result = null;
        var found = 0;

        for (var dz = -quartRadius; dz <= quartRadius; dz++)
        {
            for (var dx = -quartRadius; dx <= quartRadius; dx++)
            {
                var biome = this.biomeSource.GetNoiseBiome(sampler, quartX + dx, quartY, quartZ + dz);
                if (!biomes.Contains(biome))
                    continue;

                if (result is null || random.NextInt(found + 1) == 0)
                    result = new Vector((quartX + dx) << 2, y, (quartZ + dz) << 2);

                found++;
            }
        }

        return result;
    }

    /// <summary>Vanilla <c>ChunkPos.asLong</c>.</summary>
    private static long ChunkKey(int chunkX, int chunkZ) => (chunkX & 0xFFFFFFFFL) | (chunkZ & 0xFFFFFFFFL) << 32;

    private sealed record ReachingStarts(StructureManager Owner, int ChunkX, int ChunkZ, List<(long Key, StructureStart Start)> Starts);
}
