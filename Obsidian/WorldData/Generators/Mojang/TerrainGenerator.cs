using Obsidian.API.World.Generator.Noise;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Responsible for generating 3D terrain using density sampling and trilinear interpolation.
/// Single Responsibility: Terrain block placement based on density functions.
/// </summary>
internal class TerrainGenerator
{
    private readonly NoiseSetting settings;
    private const int HorizontalCellSize = 4;
    private const int VerticalCellSize = 8;

    public TerrainGenerator(NoiseSetting settings)
    {
        this.settings = settings;
    }

    public void Generate(IChunk chunk, IBlock defaultBlock, AquiferSystem aquiferSystem)
    {
        var slopedCheese = NoiseRegistry.DensityFunctions.Overworld.SlopedCheese;
        var finalDensity = settings.NoiseRouter.FinalDensity;
        var preliminarySurfaceLevel = settings.NoiseRouter.PreliminarySurfaceLevel;

        int cellsX = 16 / HorizontalCellSize + 1; // 5 cells
        int cellsZ = 16 / HorizontalCellSize + 1; // 5 cells
        int cellsY = (settings.Noise.Height / VerticalCellSize) + 1;

        // Sample preliminary surface levels once for the chunk (used by aquifer system)
        var surfaceLevels = SampleSurfaceLevels(chunk, preliminarySurfaceLevel);

        var densitySamples = SampleDensityGridWithSurfaceCheck(chunk, slopedCheese, finalDensity,
            preliminarySurfaceLevel, cellsX, cellsY, cellsZ);

        InterpolateAndPlaceBlocks(chunk, densitySamples, defaultBlock, aquiferSystem, surfaceLevels, cellsX, cellsY, cellsZ);
    }

    private double[,] SampleSurfaceLevels(IChunk chunk, IDensityFunction preliminarySurfaceLevel)
    {
        var surfaceLevels = new double[16, 16];
        int chunkWorldX = chunk.X << 4;
        int chunkWorldZ = chunk.Z << 4;

        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                int worldX = chunkWorldX + x;
                int worldZ = chunkWorldZ + z;
                surfaceLevels[x, z] = preliminarySurfaceLevel.GetValue(worldX, 0, worldZ);
            }
        }

        return surfaceLevels;
    }

    private double[,,] SampleDensityGridWithSurfaceCheck(IChunk chunk, IDensityFunction slopedCheese,
        IDensityFunction finalDensity, IDensityFunction preliminarySurfaceLevel, int cellsX, int cellsY, int cellsZ)
    {
        var samples = new double[cellsX, cellsY, cellsZ];
        int chunkWorldX = chunk.X << 4;
        int chunkWorldZ = chunk.Z << 4;
        int minY = settings.Noise.MinY;

        for (int ix = 0; ix < cellsX; ix++)
        {
            for (int iz = 0; iz < cellsZ; iz++)
            {
                int worldX = chunkWorldX + ix * HorizontalCellSize;
                int worldZ = chunkWorldZ + iz * HorizontalCellSize;

                // Sample the preliminary surface level once per X,Z column
                double surfaceY = preliminarySurfaceLevel.GetValue(worldX, 0, worldZ);

                for (int iy = 0; iy < cellsY; iy++)
                {
                    int worldY = minY + iy * VerticalCellSize;

                    // Above surface: use sloped_cheese (no caves)
                    // Below surface: use final_density (with caves)
                    if (worldY > surfaceY)
                    {
                        samples[ix, iy, iz] = slopedCheese.GetValue(worldX, worldY, worldZ);
                    }
                    else
                    {
                        samples[ix, iy, iz] = finalDensity.GetValue(worldX, worldY, worldZ);
                    }
                }
            }
        }

        return samples;
    }

    private double[,,] SampleDensityGrid(IChunk chunk, IDensityFunction finalDensity, int cellsX, int cellsY, int cellsZ)
    {
        var samples = new double[cellsX, cellsY, cellsZ];
        int chunkWorldX = chunk.X << 4;
        int chunkWorldZ = chunk.Z << 4;
        int minY = settings.Noise.MinY;

        for (int ix = 0; ix < cellsX; ix++)
        {
            for (int iz = 0; iz < cellsZ; iz++)
            {
                for (int iy = 0; iy < cellsY; iy++)
                {
                    int worldX = chunkWorldX + ix * HorizontalCellSize;
                    int worldY = minY + iy * VerticalCellSize;
                    int worldZ = chunkWorldZ + iz * HorizontalCellSize;

                    samples[ix, iy, iz] = finalDensity.GetValue(worldX, worldY, worldZ);
                }
            }
        }

        return samples;
    }

    private void InterpolateAndPlaceBlocks(IChunk chunk, double[,,] densitySamples, IBlock defaultBlock,
        AquiferSystem aquiferSystem, double[,] surfaceLevels, int cellsX, int cellsY, int cellsZ)
    {
        int chunkWorldX = chunk.X << 4;
        int chunkWorldZ = chunk.Z << 4;
        int minY = settings.Noise.MinY;

        for (int cellX = 0; cellX < cellsX - 1; cellX++)
        {
            for (int cellZ = 0; cellZ < cellsZ - 1; cellZ++)
            {
                for (int cellY = 0; cellY < cellsY - 1; cellY++)
                {
                    var corners = GetCellCorners(densitySamples, cellX, cellY, cellZ);
                    InterpolateCell(chunk, corners, cellX, cellY, cellZ, chunkWorldX, chunkWorldZ, minY, defaultBlock, aquiferSystem, surfaceLevels);
                }
            }
        }
    }

    private static CellCorners GetCellCorners(double[,,] samples, int cellX, int cellY, int cellZ)
    {
        return new CellCorners
        {
            D000 = samples[cellX, cellY, cellZ],
            D001 = samples[cellX, cellY, cellZ + 1],
            D010 = samples[cellX, cellY + 1, cellZ],
            D011 = samples[cellX, cellY + 1, cellZ + 1],
            D100 = samples[cellX + 1, cellY, cellZ],
            D101 = samples[cellX + 1, cellY, cellZ + 1],
            D110 = samples[cellX + 1, cellY + 1, cellZ],
            D111 = samples[cellX + 1, cellY + 1, cellZ + 1]
        };
    }

    private void InterpolateCell(IChunk chunk, CellCorners corners, int cellX, int cellY, int cellZ,
        int chunkWorldX, int chunkWorldZ, int minY, IBlock defaultBlock, AquiferSystem aquiferSystem, double[,] surfaceLevels)
    {
        for (int dx = 0; dx < HorizontalCellSize; dx++)
        {
            double fx = (double)dx / HorizontalCellSize;

            for (int dz = 0; dz < HorizontalCellSize; dz++)
            {
                double fz = (double)dz / HorizontalCellSize;

                for (int dy = 0; dy < VerticalCellSize; dy++)
                {
                    double fy = (double)dy / VerticalCellSize;

                    double density = TrilinearInterpolate(corners, fx, fy, fz);
                    int blockX = cellX * HorizontalCellSize + dx;
                    int blockY = cellY * VerticalCellSize + dy;
                    int blockZ = cellZ * HorizontalCellSize + dz;

                    if (blockX < 16 && blockZ < 16)
                    {
                        int worldY = minY + blockY;
                        int worldX = chunkWorldX + blockX;
                        int worldZ = chunkWorldZ + blockZ;

                        PlaceBlock(chunk, blockX, blockY, blockZ, worldX, worldY, worldZ, density, defaultBlock, aquiferSystem, surfaceLevels[blockX, blockZ]);
                    }
                }
            }
        }
    }

    private void PlaceBlock(IChunk chunk, int blockX, int blockY, int blockZ, int worldX, int worldY, int worldZ,
        double density, IBlock defaultBlock, AquiferSystem aquiferSystem, double preliminarySurfaceLevel)
    {
        if (density > 0.0)
        {
            chunk.SetBlock(blockX, worldY, blockZ, defaultBlock);
        }
        else
        {
            var fluid = aquiferSystem.GetFluidBlock(worldX, worldY, worldZ, preliminarySurfaceLevel);
            if (fluid != null)
            {
                chunk.SetBlock(blockX, worldY, blockZ, fluid);
            }
        }
    }

    private static double TrilinearInterpolate(CellCorners c, double tx, double ty, double tz)
    {
        // Interpolate along x
        double c00 = Lerp(tx, c.D000, c.D100);
        double c01 = Lerp(tx, c.D001, c.D101);
        double c10 = Lerp(tx, c.D010, c.D110);
        double c11 = Lerp(tx, c.D011, c.D111);

        // Interpolate along z
        double c0 = Lerp(tz, c00, c01);
        double c1 = Lerp(tz, c10, c11);

        // Interpolate along y
        return Lerp(ty, c0, c1);
    }

    private static double Lerp(double t, double a, double b) => a + t * (b - a);

    private struct CellCorners
    {
        public double D000, D001, D010, D011, D100, D101, D110, D111;
    }
}
