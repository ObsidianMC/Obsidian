using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Generators.Mojang;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// Base of the vanilla structure types (villages, strongholds...), like vanilla's <c>Structure</c>. The instances
/// vanilla defines are in the generated <c>Structures</c> registry.
/// </summary>
public abstract class Structure
{
    /// <summary>
    /// The registry id, e.g. <c>minecraft:village_plains</c>.
    /// </summary>
    public string Identifier { get; init; } = string.Empty;

    /// <summary>
    /// The structure type, e.g. <c>minecraft:jigsaw</c>.
    /// </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Biomes the structure can start in.
    /// </summary>
    public required BiomeSet Biomes { get; init; }

    /// <summary>
    /// Vanilla's mob spawn overrides inside the structure, as raw JSON (not used yet).
    /// </summary>
    public string? SpawnOverrides { get; init; }

    /// <summary>
    /// The decoration step the structure is placed in.
    /// </summary>
    public DecorationStep Step { get; init; } = DecorationStep.SurfaceStructures;

    public TerrainAdjustment TerrainAdaptation { get; init; }

    /// <summary>
    /// Vanilla <c>Structure.generate</c>: the structure's pieces if it starts in the context's chunk, else <c>null</c>.
    /// </summary>
    internal StructureStart? Generate(StructureGenerationContext context)
    {
        var stub = this.FindGenerationPoint(context);
        if (stub is null || !this.IsValidBiome(stub, context))
            return null;

        var builder = new StructurePiecesBuilder();
        stub.Generator(builder);
        return builder.IsEmpty ? null : new StructureStart(this, context.ChunkX, context.ChunkZ, builder.Build());
    }

    /// <summary>
    /// Vanilla <c>adjustBoundingBox</c>: structures that reshape terrain affect 12 blocks around their pieces.
    /// </summary>
    internal BlockBox AdjustBoundingBox(BlockBox box) => this.TerrainAdaptation != TerrainAdjustment.None ? box.InflatedBy(12) : box;

    /// <summary>
    /// Where the structure starts in the context's chunk and how to build its pieces, or <c>null</c> when it doesn't.
    /// </summary>
    internal abstract StructureStub? FindGenerationPoint(StructureGenerationContext context);

    /// <summary>
    /// Vanilla <c>afterPlace</c>: runs after the structure's pieces were placed in a chunk.
    /// </summary>
    internal virtual void AfterPlace(StructurePieceContext context, IReadOnlyList<StructurePiece> pieces)
    {
    }

    /// <summary>
    /// Vanilla <c>isValidBiome</c>: the biome source's biome at the start position must be one of <see cref="Biomes"/>.
    /// </summary>
    private bool IsValidBiome(StructureStub stub, StructureGenerationContext context)
    {
        var position = stub.Position;
        var biome = context.BiomeSource.GetNoiseBiome(context.Sampler, position.X >> 2, position.Y >> 2, position.Z >> 2);
        return context.ValidBiome(biome);
    }

    /// <summary>
    /// Vanilla <c>onTopOfChunkCenter</c>: starts at the middle of the chunk, on top of the given heightmap.
    /// </summary>
    internal static StructureStub OnTopOfChunkCenter(StructureGenerationContext context, HeightmapType heightmap, Action<StructurePiecesBuilder> generator)
    {
        var x = (context.ChunkX << 4) + 8;
        var z = (context.ChunkZ << 4) + 8;
        return new StructureStub(new Vector(x, context.Terrain.GetFirstOccupiedHeight(x, z, heightmap), z), generator);
    }

    /// <summary>
    /// Vanilla <c>getLowestY</c>: the lowest first occupied <c>WORLD_SURFACE_WG</c> height of the corners of a
    /// <paramref name="width"/> by <paramref name="depth"/> area at the chunk's minimum corner.
    /// </summary>
    internal static int GetLowestY(StructureGenerationContext context, int width, int depth) =>
        GetLowestY(context, context.ChunkX << 4, context.ChunkZ << 4, width, depth);

    internal static int GetLowestY(StructureGenerationContext context, int x, int z, int width, int depth)
    {
        var heights = GetCornerHeights(context, x, width, z, depth);
        return Math.Min(Math.Min(heights[0], heights[1]), Math.Min(heights[2], heights[3]));
    }

    /// <summary>
    /// Vanilla <c>getMeanFirstOccupiedHeight</c>: the mean of the corner heights (integer division).
    /// </summary>
    internal static int GetMeanFirstOccupiedHeight(StructureGenerationContext context, int x, int width, int z, int depth)
    {
        var heights = GetCornerHeights(context, x, width, z, depth);
        return (heights[0] + heights[1] + heights[2] + heights[3]) / 4;
    }

    /// <summary>
    /// Vanilla <c>getLowestYIn5by5BoxOffset7Blocks</c>: the lowest corner height of a 5x5 area at block 7 of the chunk,
    /// extending in the rotation's direction.
    /// </summary>
    internal static Vector GetLowestYIn5By5BoxOffset7Blocks(StructureGenerationContext context, StructureRotation rotation)
    {
        var width = rotation is StructureRotation.Clockwise90 or StructureRotation.Clockwise180 ? -5 : 5;
        var depth = rotation is StructureRotation.Clockwise180 or StructureRotation.CounterClockwise90 ? -5 : 5;
        var x = (context.ChunkX << 4) + 7;
        var z = (context.ChunkZ << 4) + 7;
        return new Vector(x, GetLowestY(context, x, z, width, depth), z);
    }

    private static int[] GetCornerHeights(StructureGenerationContext context, int x, int width, int z, int depth) =>
    [
        context.Terrain.GetFirstOccupiedHeight(x, z, HeightmapType.WorldSurfaceWG),
        context.Terrain.GetFirstOccupiedHeight(x, z + depth, HeightmapType.WorldSurfaceWG),
        context.Terrain.GetFirstOccupiedHeight(x + width, z, HeightmapType.WorldSurfaceWG),
        context.Terrain.GetFirstOccupiedHeight(x + width, z + depth, HeightmapType.WorldSurfaceWG)
    ];
}

/// <summary>
/// Placeholder for structure types Obsidian doesn't implement yet; it never starts.
/// </summary>
public sealed class UnsupportedStructure : Structure
{
    internal override StructureStub? FindGenerationPoint(StructureGenerationContext context) => null;
}

/// <summary>
/// Where a structure starts and how to build its pieces, like vanilla's <c>Structure.GenerationStub</c>.
/// </summary>
internal sealed record StructureStub(Vector Position, Action<StructurePiecesBuilder> Generator);

/// <summary>
/// What a structure sees while deciding whether and how it starts in a chunk, like vanilla's
/// <c>Structure.GenerationContext</c>.
/// </summary>
internal sealed class StructureGenerationContext
{
    public required int ChunkX { get; init; }

    public required int ChunkZ { get; init; }

    public required long Seed { get; init; }

    /// <summary>
    /// Seeded with vanilla's large feature seed for the chunk.
    /// </summary>
    public required IRandomSource Random { get; init; }

    public required IStructureTerrain Terrain { get; init; }

    public required IClimateBiomeSource BiomeSource { get; init; }

    public required ClimateSampler Sampler { get; init; }

    /// <summary>
    /// Whether the structure may start in a biome (its biomes, as checked by vanilla's structure set code).
    /// </summary>
    public required Func<BiomeCodec, bool> ValidBiome { get; init; }

    /// <summary>
    /// The dimension's build range.
    /// </summary>
    public required int MinY { get; init; }

    public required int Height { get; init; }
}

/// <summary>
/// The terrain a structure plans against before any chunk exists, from the noise alone (vanilla's chunk generator
/// height queries).
/// </summary>
internal interface IStructureTerrain
{
    public int SeaLevel { get; }

    /// <summary>
    /// Vanilla <c>getBaseHeight</c>: first free Y above the highest block matching the heightmap, from noise only.
    /// </summary>
    public int GetBaseHeight(int x, int z, HeightmapType heightmap);

    /// <summary>
    /// Vanilla <c>getFirstOccupiedHeight</c>: the Y of the highest block matching the heightmap.
    /// </summary>
    public int GetFirstOccupiedHeight(int x, int z, HeightmapType heightmap) => this.GetBaseHeight(x, z, heightmap) - 1;

    /// <summary>
    /// Vanilla <c>getBaseColumn</c>: the noise's blocks of a column, from <see cref="NoiseColumn.MinY"/> up.
    /// </summary>
    public NoiseColumn GetBaseColumn(int x, int z);
}

/// <summary>
/// The blocks of a column from the noise alone, like vanilla's <c>NoiseColumn</c>.
/// </summary>
internal sealed class NoiseColumn(int minY, IBlock[] blocks)
{
    public int MinY { get; } = minY;

    /// <summary>
    /// The block at <paramref name="y"/>; air outside the noise range.
    /// </summary>
    public IBlock GetBlock(int y) => y < this.MinY || y >= this.MinY + blocks.Length ? BlocksRegistry.Air : blocks[y - this.MinY];
}
