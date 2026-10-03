namespace Obsidian.WorldData.Structures.Processors;

/// <summary>
/// Moves template blocks onto the terrain: each block keeps its height above the template's bottom, measured from the
/// heightmap at its column, like vanilla's <c>GravityProcessor</c> (terrain matching jigsaw pieces use it).
/// </summary>
[ConfiguredFeatureProperty("minecraft:gravity")]
public sealed class GravityProcessor : StructureProcessor
{
    public HeightmapType Heightmap { get; init; } = HeightmapType.WorldSurfaceWG;

    public int Offset { get; init; }

    public override StructureBlockInfo? ProcessBlock(IWorldGenLevel level, Vector origin, Vector pivot, StructureBlockInfo original,
        StructureBlockInfo current, StructurePlaceSettings settings)
    {
        var position = current.Position;
        var height = level.GetHeight(this.Heightmap, position.X, position.Z) + this.Offset;
        return current with { Position = position with { Y = height + original.Position.Y } };
    }
}
