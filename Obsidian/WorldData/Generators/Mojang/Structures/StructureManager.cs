using Obsidian.API.Registry.Codecs.Biomes;
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

    private readonly StructureWorld world;
    private readonly IStructureTerrain terrain;
    private readonly int minY;
    private readonly int height;
    private readonly ConcurrentDictionary<ConcentricRingsStructurePlacement, Lazy<IReadOnlySet<(int X, int Z)>>> ringPositions = new();

    // The layout of the current structures, rebuilt when plugins change them (see Layout).
    private readonly Func<WorldgenStructures> structures;
    private readonly Lock layoutLock = new();
    private StructureLayout layout;

    // See FindReaching. Per manager, and its values don't reference the manager, so idle threads don't keep it alive.
    // Replaced when structures change, so idle threads don't keep the starts of plugins that are gone either: the
    // previous one lets go of its values once it's collected.
    private ThreadLocal<ReachingStarts?> lastReachingStarts = new();

    public long Seed { get; }

    /// <summary>
    /// The starts created so far, which keep the state their pieces were placed with until it's saved.
    /// </summary>
    public StructureStartCache StartCache { get; }

    // CreateStart as the cache's factory, made once: converting the method group made a delegate for every chunk a
    // lookup visits, 289 of them for each FindReaching scan, cached starts or not.
    private readonly StructureStartCache.StartFactory createStart;

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
                    Volatile.Write(ref this.layout, new StructureLayout(source, this.world.PossibleBiomes));

                return this.layout;
            }
        }
    }

    /// <param name="world">The seed, biomes and noise range structures start against.</param>
    /// <param name="startCache">Starts created before with the same seed and terrain, to keep; new ones by
    /// default.</param>
    /// <param name="structures">The structures and sets, read as structures start.</param>
    /// <param name="templates">The templates the structures start and place with where the structures' own template
    /// files don't override them (see <see cref="StructureRegistry.Use"/>).</param>
    public StructureManager(
        StructureWorld world,
        IStructureTerrain terrain,
        int minY,
        int height,
        Func<WorldgenStructures> structures,
        Func<string, StructureTemplate> templates,
        StructureStartCache? startCache = null)
    {
        this.StartCache = startCache ?? new();
        this.createStart = this.CreateStart;
        this.world = world;
        this.terrain = terrain;
        this.minY = minY;
        this.height = height;
        this.Seed = world.Seed;
        this.Templates = templates;

        this.structures = structures;
        this.layout = new StructureLayout(this.structures(), world.PossibleBiomes);
    }

    /// <summary>
    /// The templates the structures start and place with where the structures' own template files don't override
    /// them.
    /// </summary>
    public Func<string, StructureTemplate> Templates { get; }

    /// <summary>
    /// The structures that start in a chunk (vanilla's structure starts status), at most one per structure set.
    /// </summary>
    public StructureStart[] GetStarts(int chunkX, int chunkZ)
    {
        // The cache gives none for a layout older than one it was pruned for: the current one is read again.
        while (true)
        {
            var made = this.StartCache.Get(chunkX, chunkZ, this.Layout, this.createStart);
            if (made is not null)
                return made.Starts;
        }
    }

    /// <summary>
    /// Moves to the current structures, if plugins changed them, and drops the starts of structure sets that are
    /// gone, and those of plugins that are unloading, so that the plugins aren't kept.
    /// </summary>
    public void Refresh()
    {
        var layout = this.Layout;
        this.StartCache.Prune(layout.Source);
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
    public List<StructureStart> GetReferencingStarts(int chunkX, int chunkZ, Structure structure) =>
        this.FindReaching(chunkX, chunkZ).Of(structure);

    /// <summary>
    /// The starts whose box reaches the chunk, all made for the current layout: what decorating the chunk places.
    /// </summary>
    /// <remarks>
    /// Decorating a chunk asks for the starts of every structure in turn, so each thread keeps the last chunk's starts.
    /// Starts don't change once created.
    /// </remarks>
    public ReachingStarts FindReaching(int chunkX, int chunkZ)
    {
        while (true)
        {
            var layout = this.Layout;
            var cache = Volatile.Read(ref this.lastReachingStarts);
            var last = cache.Value;
            if (last is not null && last.Layout == layout && last.ChunkX == chunkX && last.ChunkZ == chunkZ)
                return last;

            if (this.TryFindReaching(layout, chunkX, chunkZ) is ReachingStarts found)
            {
                cache.Value = found;
                return found;
            }
        }
    }

    /// <summary>
    /// Every start whose box reaches the chunk, of any structure: the starts placed when the chunk is decorated.
    /// </summary>
    public IEnumerable<StructureStart> GetStartsReaching(int chunkX, int chunkZ) =>
        this.FindReaching(chunkX, chunkZ).Starts;

    // The starts reaching the chunk, made for the layout, or null once a later layout replaced it: start chunks with
    // starts of different layouts would place structures of both, or leave some out.
    private ReachingStarts? TryFindReaching(StructureLayout layout, int chunkX, int chunkZ)
    {
        var minX = chunkX << 4;
        var minZ = chunkZ << 4;
        var reaching = new List<(long, StructureStart)>();

        for (var x = chunkX - ReferenceRadius; x <= chunkX + ReferenceRadius; x++)
        {
            for (var z = chunkZ - ReferenceRadius; z <= chunkZ + ReferenceRadius; z++)
            {
                var made = this.StartCache.Get(x, z, layout, this.createStart);
                if (made is null || made.Version > layout.Version && this.Layout != layout)
                    return null;

                foreach (var start in made.Starts)
                {
                    if (start.BoundingBox.Intersects(minX, minZ, minX + 15, minZ + 15))
                        reaching.Add((ChunkKey(x, z), start));
                }
            }
        }

        return new ReachingStarts(layout, chunkX, chunkZ, reaching);
    }

    /// <summary>
    /// Loads a start chunk, waiting for it, and returns the structure starts saved in it (or <c>null</c>); set by the
    /// generator. Without it, starts are restored without saved state.
    /// </summary>
    internal Func<int, int, NbtCompound?>? LoadStartChunk { get; set; }

    /// <summary>
    /// Called with a start's start chunk once code outside of chunk decoration changed the start (see
    /// <see cref="LoadStart"/>), so the chunk saves its new state; set by the generator.
    /// </summary>
    internal Action<int, int>? StartChanged { get; set; }

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
        var reaching = this.FindReaching(chunkX, chunkZ);
        foreach (var structure in reaching.Layout.TerrainAdapting)
        {
            foreach (var start in reaching.Of(structure))
            {
                if (!start.IsRejected)
                    yield return start;
            }
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
    /// <param name="refused">Set when a structure the set picked can't start since a plugin it runs is unloading:
    /// the set's start isn't known then.</param>
    private StructureStart? CreateStart(
        StructureLayout layout,
        StructureSet set,
        int chunkX,
        int chunkZ,
        out bool refused)
    {
        refused = false;
        if (!set.Placement.IsStructureChunk(this, chunkX, chunkZ))
            return null;

        return set.Structures.Length == 1
            ? this.TryGenerate(layout, set.Structures[0].Structure, chunkX, chunkZ, out refused)
            : this.TryGenerateWeighted(layout, set.Structures, chunkX, chunkZ, out refused);
    }

    /// <summary>
    /// Picks the set's structures by weight until one starts, dropping each that doesn't.
    /// </summary>
    private StructureStart? TryGenerateWeighted(
        StructureLayout layout,
        ImmutableArray<StructureSetEntry> entries,
        int chunkX,
        int chunkZ,
        out bool refused)
    {
        refused = false;
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
            var start = this.TryGenerate(layout, chosen.Structure, chunkX, chunkZ, out refused);
            if (start is not null || refused)
                return start;

            candidates.RemoveAt(index);
            totalWeight -= chosen.Weight;
        }

        return null;
    }

    // A structure made of plugins' types runs their code as it starts, so it's a call into them; one whose plugins
    // are unloading is refused, and its failing is theirs. The start keeps its plugins, whose code its pieces run.
    private StructureStart? TryGenerate(
        StructureLayout layout,
        Structure structure,
        int chunkX,
        int chunkZ,
        out bool refused)
    {
        var owners = layout.OwnersOf(structure);
        refused = !GeneratorCalls.TryEnter(owners, out var calls);
        if (refused)
            return null;

        using (calls)
        {
            try
            {
                var start = this.Generate(layout, structure, chunkX, chunkZ);
                if (start is not null)
                    start.Owners = owners;

                return start;
            }
            catch (Exception ex) when (!owners.IsEmpty
                && ex is not (GeneratorStoppedException or PluginGenerationException or OutOfMemoryException))
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
            Math.Max(this.minY, this.world.NoiseMinY),
            Math.Min(this.height, this.world.NoiseHeight));

        var overrides = layout.Source.Templates;
        var fallback = this.Templates;
        Func<string, StructureTemplate> lookup = id => overrides.GetValueOrDefault(id) ?? fallback(id);
        using var templates = StructureRegistry.Use(lookup);
        using var featureTemplates = Generators.Features.FeatureRegistry.UseTemplates(lookup);

        return structure.Generate(new StructureGenerationContext
        {
            ChunkX = chunkX,
            ChunkZ = chunkZ,
            Seed = this.Seed,
            Random = random,
            Terrain = this.terrain,
            BiomeSource = this.world.Biomes,
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
                var biome = this.world.Biomes.GetNoiseBiome(quartX + dx, quartY, quartZ + dz);
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
}

/// <summary>
/// What structures start against: the world's seed, its biome source and the generator's noise range.
/// </summary>
/// <param name="Biomes">The biome at quart coordinates; called from several threads at once.</param>
/// <param name="PossibleBiomes">Every biome the source gives, which decides the structure sets that generate.</param>
internal sealed record StructureWorld(
    long Seed,
    IBiomeSource Biomes,
    IReadOnlyCollection<BiomeCodec> PossibleBiomes,
    int NoiseMinY,
    int NoiseHeight);
