using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// A structure that started in a chunk, with its pieces, like vanilla's <c>StructureStart</c>.
/// </summary>
public sealed class StructureStart
{
    internal StructureStart(Structure structure, int chunkX, int chunkZ, IReadOnlyList<StructurePiece> pieces)
    {
        this.Structure = structure;
        this.ChunkX = chunkX;
        this.ChunkZ = chunkZ;
        this.Pieces = pieces;
        this.BoundingBox = structure.AdjustBoundingBox(BlockBox.Encapsulating(pieces.Select(piece => piece.BoundingBox))!.Value);
    }

    public Structure Structure { get; }

    /// <summary>
    /// The chunk the structure started in.
    /// </summary>
    public int ChunkX { get; }

    /// <summary>
    /// The chunk the structure started in.
    /// </summary>
    public int ChunkZ { get; }

    public IReadOnlyList<StructurePiece> Pieces { get; }

    /// <summary>
    /// The box around every piece, grown by 12 blocks for structures that reshape terrain.
    /// </summary>
    public BlockBox BoundingBox { get; }

    /// <summary>
    /// Vanilla <c>placeInChunk</c>: places the pieces intersecting the context's box, which is the part of the chunk being
    /// decorated that may be written.
    /// </summary>
    internal void PlaceInChunk(StructurePieceContext context)
    {
        if (this.Pieces.Count == 0)
            return;

        // Pieces may orient themselves around the start piece's bottom center.
        var startBox = this.Pieces[0].BoundingBox;
        var center = startBox.Center;
        var pivotContext = context with { Pivot = new Vector(center.X, startBox.MinY, center.Z) };

        foreach (var piece in this.Pieces)
        {
            if (piece.BoundingBox.Intersects(context.Box))
                piece.PostProcess(pivotContext);
        }

        this.Structure.AfterPlace(pivotContext, this.Pieces);
    }
}

/// <summary>
/// Collects the pieces of a structure as it's built, like vanilla's <c>StructurePiecesBuilder</c>.
/// </summary>
public sealed class StructurePiecesBuilder : IStructurePieceAccessor
{
    private readonly List<StructurePiece> pieces = [];

    public bool IsEmpty => this.pieces.Count == 0;

    public IReadOnlyList<StructurePiece> Pieces => this.pieces;

    public void AddPiece(StructurePiece piece) => this.pieces.Add(piece);

    public StructurePiece? FindCollisionPiece(BlockBox box) => StructurePiece.FindCollisionPiece(this.pieces, box);

    /// <summary>
    /// Vanilla <c>offsetPiecesVertically</c>: moves every piece up by <paramref name="offset"/>.
    /// </summary>
    public void OffsetPiecesVertically(int offset)
    {
        foreach (var piece in this.pieces)
            piece.Move(0, offset, 0);
    }

    /// <summary>
    /// Vanilla <c>moveBelowSeaLevel</c>: sinks the structure so its top sits at a random depth below sea level.
    /// </summary>
    /// <returns>The vertical offset applied.</returns>
    public int MoveBelowSeaLevel(int seaLevel, int minY, IRandomSource random, int offset)
    {
        var maxY = seaLevel - offset;
        var box = this.GetBoundingBox();
        var y = box.YSpan + minY + 1;
        if (y < maxY)
            y += random.NextInt(maxY - y);

        var shift = y - box.MaxY;
        this.OffsetPiecesVertically(shift);
        return shift;
    }

    /// <summary>
    /// Vanilla <c>moveInsideHeights</c>: moves the structure to a random height between <paramref name="lowestAllowed"/> and
    /// <paramref name="highestAllowed"/>.
    /// </summary>
    public void MoveInsideHeights(IRandomSource random, int lowestAllowed, int highestAllowed)
    {
        var box = this.GetBoundingBox();
        var maxStart = highestAllowed - lowestAllowed + 1 - box.YSpan;
        var y = maxStart > 1 ? lowestAllowed + random.NextInt(maxStart) : lowestAllowed;
        this.OffsetPiecesVertically(y - box.MinY);
    }

    public BlockBox GetBoundingBox() => BlockBox.Encapsulating(this.pieces.Select(piece => piece.BoundingBox))
        ?? throw new InvalidOperationException("Unable to calculate a bounding box without pieces.");

    public IReadOnlyList<StructurePiece> Build() => [.. this.pieces];

    public void Clear() => this.pieces.Clear();
}

/// <summary>
/// Where pieces add other pieces while a structure is built, like vanilla's <c>StructurePieceAccessor</c>.
/// </summary>
public interface IStructurePieceAccessor
{
    public void AddPiece(StructurePiece piece);

    /// <summary>
    /// The first piece whose box intersects <paramref name="box"/>, or <c>null</c>.
    /// </summary>
    public StructurePiece? FindCollisionPiece(BlockBox box);
}

/// <summary>
/// What a piece sees while it's placed in a chunk, like the arguments of vanilla's <c>StructurePiece.postProcess</c>.
/// </summary>
/// <param name="Level">The chunks around the chunk being decorated.</param>
/// <param name="Random">Seeded with the structure's feature seed for the chunk.</param>
/// <param name="Box">The part of the chunk being decorated that may be written.</param>
/// <param name="Pivot">The bottom center of the structure's first piece.</param>
public sealed record StructurePieceContext(IWorldGenLevel Level, IRandomSource Random, BlockBox Box, int ChunkX, int ChunkZ, Vector Pivot)
{
    internal IStructureTerrain Terrain { get; init; } = default!;
}
