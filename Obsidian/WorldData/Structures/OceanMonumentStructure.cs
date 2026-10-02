using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// The prismarine ocean monument guarded by elder guardians, like vanilla's <c>OceanMonumentStructure</c>.
/// </summary>
[StructureType("minecraft:ocean_monument")]
public sealed class OceanMonumentStructure : Structure
{
    private static readonly BiomeSet requiredSurrounding = new("#minecraft:required_ocean_monument_surrounding");

    internal override StructureStub? FindGenerationPoint(StructureGenerationContext context)
    {
        var x = (context.ChunkX << 4) + 9;
        var z = (context.ChunkZ << 4) + 9;
        if (!IsSurroundedByOcean(context, x, context.Terrain.SeaLevel, z, OceanMonumentPieces.MonumentBuilding.BiomeRangeCheck))
            return null;

        return OnTopOfChunkCenter(context, HeightmapType.OceanFloorWG, builder =>
        {
            var orientation = FeatureHelpers.RandomHorizontal(context.Random);
            builder.AddPiece(new OceanMonumentPieces.MonumentBuilding(context.Random, (context.ChunkX << 4) - 29, (context.ChunkZ << 4) - 29,
                orientation));
        });
    }

    /// <summary>
    /// Vanilla's <c>getBiomesWithin</c> check: every biome cell within <paramref name="radius"/> blocks must allow a monument
    /// around it.
    /// </summary>
    private static bool IsSurroundedByOcean(StructureGenerationContext context, int x, int y, int z, int radius)
    {
        for (var quartZ = (z - radius) >> 2; quartZ <= (z + radius) >> 2; quartZ++)
        {
            for (var quartX = (x - radius) >> 2; quartX <= (x + radius) >> 2; quartX++)
            {
                for (var quartY = (y - radius) >> 2; quartY <= (y + radius) >> 2; quartY++)
                {
                    if (!requiredSurrounding.Contains(context.BiomeSource.GetNoiseBiome(context.Sampler, quartX, quartY, quartZ)))
                        return false;
                }
            }
        }

        return true;
    }
}
