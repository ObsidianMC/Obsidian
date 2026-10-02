using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Nbt;
using System.Threading;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// A structure that started in a chunk, with its pieces, like vanilla's <c>StructureStart</c>.
/// </summary>
public sealed class StructureStart
{
    // Pieces change as they're placed, and a start is shared by every chunk it reaches, which may be decorated in parallel.
    // Placing, saving and restoring the pieces' state take this lock, so a save never sees a half-placed piece.
    private readonly Lock stateLock = new();

    private bool stateRestored;

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
    /// How many times a search for unknown structures found this start (vanilla's <c>references</c>).
    /// </summary>
    public int References => this.references;

    private int references;

    /// <summary>
    /// Vanilla <c>canBeReferenced</c> then <c>addReference</c>: claims the start for a search that skips known structures,
    /// which each start allows once.
    /// </summary>
    /// <remarks>
    /// The count is saved with the start's state, so the start must be restored first (see
    /// <see cref="Generators.Mojang.Structures.StructureManager.LoadStart"/>).
    /// </remarks>
    internal bool TryAddReference()
    {
        while (true)
        {
            var current = this.references;
            if (current >= 1)
                return false;

            if (Interlocked.CompareExchange(ref this.references, current + 1, current) == current)
                return true;
        }
    }

    private static void InterlockedMax(ref int location, int value)
    {
        var current = Volatile.Read(ref location);
        while (current < value)
        {
            var previous = Interlocked.CompareExchange(ref location, value, current);
            if (previous == current)
                return;

            current = previous;
        }
    }

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

        using var scope = this.stateLock.EnterScope();

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

    /// <summary>
    /// Whether <see cref="RestoreState"/> ran, so this start's pieces hold the state to save.
    /// </summary>
    internal bool IsStateRestored
    {
        get
        {
            using var scope = this.stateLock.EnterScope();
            return this.stateRestored;
        }
    }

    /// <summary>
    /// Saves the start like vanilla's <c>StructureStart.createTag</c> (<c>id</c>, <c>ChunkX</c>, <c>ChunkZ</c>,
    /// <c>Children</c>), with each piece's placement state (see <see cref="StructurePiece.SaveState"/>) rather than the whole
    /// piece.
    /// </summary>
    internal NbtCompound SaveState()
    {
        var children = new NbtList(NbtTagType.Compound, "Children");

        using (this.stateLock.EnterScope())
        {
            foreach (var piece in this.Pieces)
            {
                var child = new NbtCompound();
                piece.SaveState(child);
                children.Add(child);
            }
        }

        return new NbtCompound(this.Structure.Identifier)
        {
            new NbtTag<string>("id", this.Structure.Identifier),
            new NbtTag<int>("ChunkX", this.ChunkX),
            new NbtTag<int>("ChunkZ", this.ChunkZ),
            new NbtTag<int>("references", this.References),
            children
        };
    }

    /// <summary>
    /// Restores the pieces' state from what <see cref="SaveState"/> saved, or from nothing when the start was never saved.
    /// Only the first call does anything; it must happen before the start is first placed.
    /// </summary>
    /// <remarks>
    /// Saved state for a different set of pieces (from another version of the structure) is ignored.
    /// </remarks>
    internal void RestoreState(NbtCompound? saved)
    {
        using var scope = this.stateLock.EnterScope();

        if (this.stateRestored)
            return;

        this.stateRestored = true;

        if (saved is null)
            return;

        // References only ever grow, so one taken already isn't undone.
        if (saved.TryGetTag<NbtTag<int>>("references", out var references))
            InterlockedMax(ref this.references, references.Value);

        if (!saved.TryGetTag<NbtList>("Children", out var children) || children.Count != this.Pieces.Count)
            return;

        for (var i = 0; i < children.Count; i++)
        {
            if (children[i] is NbtCompound child)
                this.Pieces[i].LoadState(child);
        }
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

    /// <summary>
    /// The vertical range features placed by pieces resolve their anchors in (vanilla's <c>WorldGenerationContext</c>).
    /// </summary>
    internal WorldGenerationContext Generation { get; init; }
}
