using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Structures.Placement;

/// <summary>
/// Decides which chunks a structure set may start in, like vanilla's <c>StructurePlacement</c>.
/// </summary>
public abstract class StructurePlacement
{
    // Vanilla's StructurePlacement.HIGHLY_ARBITRARY_RANDOM_SALT, used by legacy_type_2.
    private const int ArbitrarySalt = 10387320;

    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Offset from a start chunk's minimum corner reported when locating the structure.
    /// </summary>
    public Vector LocateOffset { get; init; } = Vector.Zero;

    public FrequencyReductionMethod FrequencyReductionMethod { get; init; } = FrequencyReductionMethod.Default;

    /// <summary>
    /// Chance that a chunk passing the placement keeps its start.
    /// </summary>
    public float Frequency { get; init; } = 1.0f;

    public int Salt { get; init; }

    /// <summary>
    /// Keeps this set away from chunks where another set starts.
    /// </summary>
    public StructureExclusionZone? ExclusionZone { get; init; }

    /// <summary>
    /// Vanilla <c>isStructureChunk</c>: whether the set may start in the chunk.
    /// </summary>
    internal bool IsStructureChunk(IStructurePlacementState state, int chunkX, int chunkZ) =>
        this.IsPlacementChunk(state, chunkX, chunkZ)
        && this.ApplyAdditionalChunkRestrictions(chunkX, chunkZ, state.Seed)
        && (this.ExclusionZone is null
            || !state.HasStructureChunkInRange(this.ExclusionZone.OtherSet, chunkX, chunkZ, this.ExclusionZone.ChunkCount));

    private protected abstract bool IsPlacementChunk(IStructurePlacementState state, int chunkX, int chunkZ);

    private bool ApplyAdditionalChunkRestrictions(int chunkX, int chunkZ, long seed) =>
        this.Frequency >= 1.0f || this.FrequencyReductionMethod switch
        {
            FrequencyReductionMethod.LegacyType1 => LegacyPillagerOutpostReducer(seed, chunkX, chunkZ, this.Frequency),
            FrequencyReductionMethod.LegacyType2 => LargeFeatureRandom(seed, chunkX, chunkZ, ArbitrarySalt).NextFloat() < this.Frequency,
            FrequencyReductionMethod.LegacyType3 => LegacyDoubleReducer(seed, chunkX, chunkZ, this.Frequency),
            _ => LargeFeatureRandom(seed, chunkX, chunkZ, this.Salt).NextFloat() < this.Frequency
        };

    private static WorldgenRandom LargeFeatureRandom(long seed, int chunkX, int chunkZ, int salt)
    {
        var random = new WorldgenRandom(new LegacyRandomSource(0L));
        random.SetLargeFeatureWithSalt(seed, chunkX, chunkZ, salt);
        return random;
    }

    private static bool LegacyDoubleReducer(long seed, int chunkX, int chunkZ, float frequency)
    {
        var random = new WorldgenRandom(new LegacyRandomSource(0L));
        random.SetLargeFeatureSeed(seed, chunkX, chunkZ);
        return random.NextDouble() < frequency;
    }

    private static bool LegacyPillagerOutpostReducer(long seed, int chunkX, int chunkZ, float frequency)
    {
        var random = new WorldgenRandom(new LegacyRandomSource(0L));
        random.SetSeed((chunkX >> 4) ^ (chunkZ >> 4) << 4 ^ seed);
        random.NextInt();
        return random.NextInt((int)(1.0f / frequency)) == 0;
    }
}

/// <summary>
/// Keeps a structure set at least <see cref="ChunkCount"/> chunks away from chunks where <see cref="OtherSet"/> may start,
/// like vanilla's <c>StructurePlacement.ExclusionZone</c>.
/// </summary>
public sealed class StructureExclusionZone
{
    public required StructureSet OtherSet { get; init; }

    public required int ChunkCount { get; init; }
}

/// <summary>
/// How <see cref="StructurePlacement.Frequency"/> thins out start chunks, like vanilla's <c>FrequencyReductionMethod</c>.
/// </summary>
public enum FrequencyReductionMethod
{
    Default,

    /// <summary>Pillager outposts' pre-1.18 check.</summary>
    LegacyType1,

    /// <summary>Uses a fixed salt instead of the placement's.</summary>
    LegacyType2,

    /// <summary>Uses the large feature seed and a double.</summary>
    LegacyType3
}

/// <summary>
/// What placements need from the world's structure state, like vanilla's <c>ChunkGeneratorStructureState</c>.
/// </summary>
internal interface IStructurePlacementState
{
    public long Seed { get; }

    /// <summary>
    /// The start chunks of a concentric rings placement.
    /// </summary>
    public IReadOnlySet<(int X, int Z)> GetRingPositions(ConcentricRingsStructurePlacement placement);

    /// <summary>
    /// Whether <paramref name="set"/>'s placement accepts a chunk within <paramref name="range"/> chunks.
    /// </summary>
    public bool HasStructureChunkInRange(StructureSet set, int chunkX, int chunkZ, int range);
}
