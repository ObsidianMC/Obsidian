namespace Obsidian.WorldData.Structures;

/// <summary>
/// Where a jigsaw piece connects to the next one, like vanilla's <c>JigsawJunction</c>. The beardifier smooths the
/// terrain around junctions.
/// </summary>
/// <param name="SourceGroundY">The ground height at the connection.</param>
/// <param name="DeltaY">The height difference to the connected piece.</param>
/// <param name="DestinationRigid">Whether the connected piece keeps its shape (rigid projection).</param>
public readonly record struct JigsawJunction(int SourceX, int SourceGroundY, int SourceZ, int DeltaY, bool DestinationRigid);

/// <summary>
/// What the beardifier needs from a jigsaw piece (vanilla's <c>PoolElementStructurePiece</c>).
/// </summary>
public interface IJigsawPiece
{
    /// <summary>
    /// Whether the piece keeps its shape (rigid projection) rather than following the terrain.
    /// </summary>
    public bool IsRigid { get; }

    /// <summary>
    /// How far above the piece's bottom the ground is meant to be.
    /// </summary>
    public int GroundLevelDelta { get; }

    public IReadOnlyList<JigsawJunction> Junctions { get; }
}
