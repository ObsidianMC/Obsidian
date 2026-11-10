namespace Obsidian.WorldData.Features;

/// <summary>
/// Reads blocks from a generating level or from a shape rule's view (<see cref="IShapeLevel"/>), for the survival and
/// shape checks generation and the shape rules share (<see cref="BlockSurvival"/>). It holds one of the two; reading
/// through it allocates nothing.
/// </summary>
internal readonly struct BlockReader
{
    private readonly IWorldGenLevel? generation;

    private readonly IShapeLevel? shape;

    public BlockReader(IWorldGenLevel level) => this.generation = level;

    public BlockReader(IShapeLevel level) => this.shape = level;

    /// <summary>The block at <paramref name="position"/>.</summary>
    public IBlock GetBlock(Vector position) =>
        this.generation is not null ? this.generation.GetBlock(position) : this.shape!.GetBlock(position);
}
