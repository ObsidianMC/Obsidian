using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.WorldData.Structures;
using Obsidian.WorldData.Structures.Placement;
using System.Threading;

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
/// <para>
/// The structures and sets are the current <see cref="WorldgenStructures"/>' (see <see cref="Layout"/>): when plugins
/// change them, chunks whose starts weren't made yet get the new sets' starts (see <see cref="StructureStartCache"/>).
/// Starting and placing a structure made of plugins' types is a call into those plugins.
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
    private readonly ConcurrentDictionary<ConcentricRingsStructurePlacement, Lazy<IReadOnlySet<(int X, int Z)>>> ringPositions = new();

    // The layout of the current structures, rebuilt when plugins change them (see Layout).
    private readonly Func<WorldgenStructures> structures;
    private readonly Lock layoutLock = new();
    private StructureLayout layout;

    // See GetReachingStarts. Per manager, and its values don't reference the manager, so idle threads don't keep it alive.
    // Replaced when structures change, so idle threads don't keep the starts of plugins that are gone either: the
    // previous one lets go of its values once it's collected.
    private ThreadLocal<ReachingStarts?> lastReachingStarts = new();

    public long Seed { get; }

    /// <summary>
    /// The starts created so far, which keep the state their pieces were placed with until it's saved.
    /// </summary>
    public StructureStartCache StartCache { get; }

    /// <summary>
    /// The structure sets of the dimension, in registry order.
    /// </summary>
    public IReadOnlyList<StructureSet> Sets => this.Layout.Sets;

    /// <summary>
    /// The structures of each decoration step in registry order, which decides their feature seed index.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<Structure>> StructuresPerStep => this.Layout.StructuresPerStep;

    /// <summary>
    /// The current structures' layout for the dimension, rebuilt when plugins changed them. Code going through
    /// several structures reads it once, so they're all of the same layout.
    /// </summary>
    public StructureLayout Layout
    {
        get
        {
            var source = this.structures();
            var current = Volatile.Read(ref this.layout);
            if (current.Version >= source.Version)
                return current;

            lock (this.layoutLock)
            {
                // A slower thread mustn't put back structures older than another's.
                if (this.layout.Version < source.Version)
                    Volatile.Write(ref this.layout, new StructureLayout(source, this.biomeSource.PossibleBiomes));

                return this.layout;
            }
        }
    }

    /// <param name="startCache">Starts created before with the same seed and terrain, to keep; new ones by
    /// default.</param>
    /// <param name="structures">The structures and sets, read as structures start;
    /// <see cref="PluginStructures.Current"/> by default.</param>
    public StructureManager(
        RandomState randomState,
        IClimateBiomeSource biomeSource,
        IStructureTerrain terrain,
        int minY,
        int height,
        StructureStartCache? startCache = null,
        Func<WorldgenStructures>? structures = null)
    {
        this.StartCache = startCache ?? new();
        this.randomState = randomState;
        this.biomeSource = biomeSource;
        this.terrain = terrain;
        this.minY = minY;
        this.height = height;
        this.Seed = randomState.Seed;

        this.structures = structures ?? (static () => PluginStructures.Current);
        this.layout = new StructureLayout(this.structures(), biomeSource.PossibleBiomes);
    }

    /// <summary>
    /// The structures that start in a chunk (vanilla's structure starts status), at most one per structure set.
    /// </summary>
    public StructureStart[] GetStarts(int chunkX, int chunkZ) =>
        this.StartCache.Get(chunkX, chunkZ, this.Layout, this.CreateStart);

    /// <summary>
    /// Moves to the current structures, if plugins changed them, and drops the starts of structure sets that are
    /// gone, so that the plugins they came from aren't kept.
    /// </summary>
    public void Refresh()
    {
        var layout = this.Layout;
        this.StartCache.Prune(layout.Source.Sets.Values.ToHashSet());
        Volatile.Write(ref this.lastReachingStarts, new ThreadLocal<ReachingStarts?>());

        foreach (var placement in this.ringPositions.Keys)
        {
            if (!layout.Sets.Any(set => set.Placement == placement))
                this.ringPositions.TryRemove(placement, out _);
        }
    }

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
        var version = this.Layout.Version;
        var cache = Volatile.Read(ref this.lastReachingStarts);
        var last = cache.Value;
        if (last is not null && last.Version == version && last.ChunkX == chunkX && last.ChunkZ == chunkZ)
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

        cache.Value = new ReachingStarts(version, chunkX, chunkZ, reaching);
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
    /// The structure starts to save in their start chunk (see <see cref="StructureStartCache.Save"/>).
    /// </summary>
    public NbtCompound? SaveStarts(int chunkX, int chunkZ, NbtCompound? loaded) =>
        this.StartCache.Save(chunkX, chunkZ, loaded);

    /// <summary>
    /// Every start of a structure with terrain adaptation whose box reaches the chunk, for the beardifier.
    /// </summary>
    /// <remarks>
    /// Vanilla goes through the structures in hash map order, which isn't stable between runs; registry order is used here.
    /// </remarks>
    public IEnumerable<StructureStart> GetTerrainAdaptingStarts(int chunkX, int chunkZ)
    {
        foreach (var structure in this.Layout.TerrainAdapting)
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
    /// Vanilla <c>ChunkGenerator.createStructures</c> for one structure set in one chunk.
    /// </summary>
    private StructureStart? CreateStart(StructureLayout layout, StructureSet set, int chunkX, int chunkZ)
    {
        if (!set.Placement.IsStructureChunk(this, chunkX, chunkZ))
            return null;

        return set.Structures.Length == 1
            ? this.TryGenerate(layout, set.Structures[0].Structure, chunkX, chunkZ)
            : this.TryGenerateWeighted(layout, set.Structures, chunkX, chunkZ);
    }

    /// <summary>
    /// Picks the set's structures by weight until one starts, dropping each that doesn't.
    /// </summary>
    private StructureStart? TryGenerateWeighted(
        StructureLayout layout,
        ImmutableArray<StructureSetEntry> entries,
        int chunkX,
        int chunkZ)
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
            var start = this.TryGenerate(layout, chosen.Structure, chunkX, chunkZ);
            if (start is not null)
                return start;

            candidates.RemoveAt(index);
            totalWeight -= chosen.Weight;
        }

        return null;
    }

    // A structure made of plugins' types runs their code as it starts, so it's a call into them; one whose plugins
    // are unloading doesn't start, and its failing is theirs.
    private StructureStart? TryGenerate(StructureLayout layout, Structure structure, int chunkX, int chunkZ)
    {
        var owners = layout.OwnersOf(structure);
        if (!GeneratorCalls.TryEnter(owners, out var calls))
            return null;

        using (calls)
        {
            try
            {
                return this.Generate(layout, structure, chunkX, chunkZ);
            }
            catch (Exception ex) when (!owners.IsEmpty && ex is not (GeneratorStoppedException or OutOfMemoryException))
            {
                throw new PluginGenerationException(
                    owners,
                    $"Failed to start structure '{structure.Identifier}' in chunk ({chunkX}, {chunkZ}).",
                    ex);
            }
        }
    }

    private StructureStart? Generate(StructureLayout layout, Structure structure, int chunkX, int chunkZ)
    {
        var random = new WorldgenRandom(new LegacyRandomSource(0L));
        random.SetLargeFeatureSeed(this.Seed, chunkX, chunkZ);

        var generation = new WorldGenerationContext(
            Math.Max(this.minY, this.randomState.Settings.Noise.MinY),
            Math.Min(this.height, this.randomState.Settings.Noise.Height));

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
            Generation = generation,
            Pools = layout.Source.Pool
        });
    }

    /// <summary>
    /// Vanilla <c>ChunkGeneratorStructureState.generateRingPositions</c>: starts spread over rings, each moved to a preferred
    /// biome found within 112 blocks.
    /// </summary>
    private HashSet<(int X, int Z)> CreateRingPositions(ConcentricRingsStructurePlacement placement)
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

    private sealed record ReachingStarts(
        long Version,
        int ChunkX,
        int ChunkZ,
        List<(long Key, StructureStart Start)> Starts);
}
