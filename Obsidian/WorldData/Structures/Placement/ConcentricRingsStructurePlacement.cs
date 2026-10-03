namespace Obsidian.WorldData.Structures.Placement;

/// <summary>
/// A fixed number of starts on rings around the origin, moved towards preferred biomes (strongholds), like vanilla's
/// <c>ConcentricRingsStructurePlacement</c>. The ring positions are computed once per world.
/// </summary>
[StructureType("minecraft:concentric_rings")]
public sealed class ConcentricRingsStructurePlacement : StructurePlacement
{
    /// <summary>
    /// Ring spacing, in units of 6 chunks.
    /// </summary>
    public required int Distance { get; init; }

    /// <summary>
    /// Number of starts on the first ring.
    /// </summary>
    public required int Spread { get; init; }

    /// <summary>
    /// Total number of starts.
    /// </summary>
    public required int Count { get; init; }

    /// <summary>
    /// Biomes each start is moved into when one is found nearby.
    /// </summary>
    public required BiomeSet PreferredBiomes { get; init; }

    private protected override bool IsPlacementChunk(IStructurePlacementState state, int chunkX, int chunkZ) =>
        state.GetRingPositions(this).Contains((chunkX, chunkZ));
}
