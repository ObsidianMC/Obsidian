using Obsidian.WorldData.Structures;

namespace Obsidian.WorldData.Generators.Mojang.Structures;

/// <summary>
/// The structure starts whose box reaches a chunk, all made for one <see cref="StructureLayout"/>: decorating the
/// chunk restores, places and saves these, whatever plugins change meanwhile (see
/// <see cref="StructureManager.FindReaching"/>).
/// </summary>
internal sealed class ReachingStarts
{
    // The starts with their start chunk's key, start chunk by start chunk (X, then Z).
    private readonly List<(long Key, StructureStart Start)> starts;

    public ReachingStarts(StructureLayout layout, int chunkX, int chunkZ, List<(long Key, StructureStart Start)> starts)
    {
        this.Layout = layout;
        this.ChunkX = chunkX;
        this.ChunkZ = chunkZ;
        this.starts = starts;
    }

    /// <summary>
    /// The layout the starts were made for, whose structures decoration places in its order.
    /// </summary>
    public StructureLayout Layout { get; }

    public int ChunkX { get; }

    public int ChunkZ { get; }

    /// <summary>
    /// Every start, start chunk by start chunk.
    /// </summary>
    public IEnumerable<StructureStart> Starts => this.starts.Select(entry => entry.Start);

    /// <summary>
    /// Whether a start of the structure reaches the chunk.
    /// </summary>
    public bool Reaches(Structure structure) => this.starts.Exists(entry => entry.Start.Structure == structure);

    /// <summary>
    /// The starts of <paramref name="structure"/>, in the order vanilla places them.
    /// </summary>
    public List<StructureStart> Of(Structure structure)
    {
        // Vanilla adds the reference chunks to a LongOpenHashSet in this order and places the starts in the set's
        // order.
        List<long>? references = null;
        Dictionary<long, StructureStart>? byChunk = null;

        foreach (var (key, start) in this.starts)
        {
            if (start.Structure != structure)
                continue;

            (references ??= []).Add(key);
            (byChunk ??= [])[key] = start;
        }

        return references is null ? [] : [.. LongHashSetOrder.Order(references).Select(key => byChunk![key])];
    }
}
