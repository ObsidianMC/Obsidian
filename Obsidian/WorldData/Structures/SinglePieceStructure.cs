using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// A structure made of one piece placed at the chunk's corner, above sea level, like vanilla's
/// <c>SinglePieceStructure</c> (temples).
/// </summary>
public abstract class SinglePieceStructure : Structure
{
    private readonly int width;
    private readonly int depth;

    protected SinglePieceStructure(int width, int depth)
    {
        this.width = width;
        this.depth = depth;
    }

    internal override StructureStub? FindGenerationPoint(StructureGenerationContext context) =>
        GetLowestY(context, this.width, this.depth) < context.Terrain.SeaLevel
            ? null
            : OnTopOfChunkCenter(context, HeightmapType.WorldSurfaceWG,
                builder => builder.AddPiece(this.CreatePiece(context.Random, context.ChunkX << 4, context.ChunkZ << 4)));

    /// <summary>
    /// Creates the structure's piece with its corner at (<paramref name="x"/>, <paramref name="z"/>).
    /// </summary>
    protected abstract StructurePiece CreatePiece(IRandomSource random, int x, int z);
}
