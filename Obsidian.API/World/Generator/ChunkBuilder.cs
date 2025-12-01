using Obsidian.API.Registries;
using Obsidian.API.World.Generator.DensityFunctions;
using Obsidian.API.World.Generator.Noise;
using System.Security.Cryptography;
using System.Text;

namespace Obsidian.API.World.Generator;

public class ChunkBuilder
{
    private readonly IWorld world;

    internal int Seed { get; private set; }

    private readonly NoiseSetting Settings;
    private readonly double positiveNoiseFactor;
    private readonly double negativeNoiseFactor;

    private IDensityFunction baseDensity;

    public ChunkBuilder(IWorld world, string noiseSettingTag)
    {
        this.world = world;
        if (!int.TryParse(world.Seed, out int seedHash))
            seedHash = BitConverter.ToInt32(MD5.HashData(Encoding.UTF8.GetBytes(world.Seed)));
        Seed = seedHash;
        var all = NoiseRegistry.NoiseSettings.All;
        Settings = NoiseRegistry.NoiseSettings.All[noiseSettingTag];

        positiveNoiseFactor = Settings.Noise.Height + Settings.Noise.MinY - Settings.SeaLevel;
        negativeNoiseFactor = Settings.Noise.Height - positiveNoiseFactor - Settings.SeaLevel;

        var aquiferBarrierNoise = Settings.NoiseRouter.Barrier;
        var aquiferFluidLevelFloodednessNoise = Settings.NoiseRouter.FluidLevelFloodedness;
        var aquiferFluidLevelSpreadNoise = Settings.NoiseRouter.FluidLevelSpread;
        var temperatureNoise = Settings.NoiseRouter.Temperature;
        var vegetationNoise = Settings.NoiseRouter.Vegetation;
        //THis is breaking things idk why
        //var terrainFactor = NoiseRegistry.DensityFunctions.Overworld.Factor;
        //var terrainDepth = NoiseRegistry.DensityFunctions.Overworld.Depth;
        //baseDensity = NoiseGradientDensity(new Cache2DDensityFunction() { Argument = terrainFactor }, terrainDepth);
    }

    private static IDensityFunction NoiseGradientDensity(IDensityFunction a, IDensityFunction b)
    {
        return new MulDensityFunction()
        {
            Argument1 = new ConstantDensityFunction() { Argument = 4.0 },
            Argument2 = new QuarterNegativeDensityFunction()
            {
                Argument = new MulDensityFunction() { Argument1 = a, Argument2 = b }
            }
        };
    }


    public void InitialShape(IChunk chunk, IBlock fill)
    {
        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                var val = baseDensity.GetValue(((chunk.X << 4) + x) * 4, 0, ((chunk.Z << 4) + z) * 4);
                val = (val - 5.0) / 5.0;
                int y = NoiseToY(val);
                chunk.SetBlock(x, y, z, fill);

            }
        }
    }

    /// <summary>
    /// Generates terrain using 3D density sampling - the core of Mojang's world generation.
    /// Samples density functions on a coarse grid and interpolates between samples.
    /// </summary>
    /// <param name="chunk">The chunk to generate terrain in</param>
    /// <param name="defaultBlock">The default block to use for solid terrain (usually stone)</param>
    public void Generate3DTerrain(IChunk chunk, IBlock defaultBlock)
    {
        var finalDensity = Settings.NoiseRouter.FinalDensity;

        // Minecraft samples every 4 blocks horizontally and 8 blocks vertically
        const int horizontalCellSize = 4;
        const int verticalCellSize = 8;

        // Calculate grid dimensions
        int cellsX = 16 / horizontalCellSize + 1; // 5 cells (0, 4, 8, 12, 16)
        int cellsZ = 16 / horizontalCellSize + 1; // 5 cells
        int cellsY = (Settings.Noise.Height / verticalCellSize) + 1; // e.g., 384/8 + 1 = 49 cells

        // Sample density at grid points
        double[,,] densitySamples = new double[cellsX, cellsY, cellsZ];

        int chunkWorldX = chunk.X << 4;
        int chunkWorldZ = chunk.Z << 4;
        int minY = Settings.Noise.MinY;

        // Sample at grid points
        for (int ix = 0; ix < cellsX; ix++)
        {
            for (int iz = 0; iz < cellsZ; iz++)
            {
                for (int iy = 0; iy < cellsY; iy++)
                {
                    int worldX = chunkWorldX + ix * horizontalCellSize;
                    int worldY = minY + iy * verticalCellSize;
                    int worldZ = chunkWorldZ + iz * horizontalCellSize;

                    densitySamples[ix, iy, iz] = finalDensity.GetValue(worldX, worldY, worldZ);
                }
            }
        }

        // Interpolate and place blocks
        for (int cellX = 0; cellX < cellsX - 1; cellX++)
        {
            for (int cellZ = 0; cellZ < cellsZ - 1; cellZ++)
            {
                for (int cellY = 0; cellY < cellsY - 1; cellY++)
                {
                    // Get the 8 corner densities for this cell
                    double d000 = densitySamples[cellX, cellY, cellZ];
                    double d001 = densitySamples[cellX, cellY, cellZ + 1];
                    double d010 = densitySamples[cellX, cellY + 1, cellZ];
                    double d011 = densitySamples[cellX, cellY + 1, cellZ + 1];
                    double d100 = densitySamples[cellX + 1, cellY, cellZ];
                    double d101 = densitySamples[cellX + 1, cellY, cellZ + 1];
                    double d110 = densitySamples[cellX + 1, cellY + 1, cellZ];
                    double d111 = densitySamples[cellX + 1, cellY + 1, cellZ + 1];

                    // Interpolate within the cell
                    for (int dx = 0; dx < horizontalCellSize; dx++)
                    {
                        double fx = (double)dx / horizontalCellSize;

                        for (int dz = 0; dz < horizontalCellSize; dz++)
                        {
                            double fz = (double)dz / horizontalCellSize;

                            for (int dy = 0; dy < verticalCellSize; dy++)
                            {
                                double fy = (double)dy / verticalCellSize;

                                // Trilinear interpolation
                                double density = TrilinearInterpolate(
                                    d000, d001, d010, d011,
                                    d100, d101, d110, d111,
                                    fx, fy, fz
                                );

                                int blockX = cellX * horizontalCellSize + dx;
                                int blockY = cellY * verticalCellSize + dy;
                                int blockZ = cellZ * horizontalCellSize + dz;

                                // Only place blocks within chunk bounds
                                if (blockX < 16 && blockZ < 16)
                                {
                                    int worldY = minY + blockY;

                                    // Density > 0 means solid block
                                    if (density > 0.0)
                                    {
                                        // Use default block (usually stone)
                                        // Note: Surface rules should be applied later to determine actual surface blocks
                                        chunk.SetBlock(blockX, worldY, blockZ, defaultBlock);
                                    }
                                    // Could add fluid placement here for aquifers
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Performs trilinear interpolation between 8 corner values of a cube.
    /// </summary>
    private static double TrilinearInterpolate(
        double c000, double c001, double c010, double c011,
        double c100, double c101, double c110, double c111,
        double tx, double ty, double tz)
    {
        // Interpolate along x
        double c00 = Lerp(tx, c000, c100);
        double c01 = Lerp(tx, c001, c101);
        double c10 = Lerp(tx, c010, c110);
        double c11 = Lerp(tx, c011, c111);

        // Interpolate along z
        double c0 = Lerp(tz, c00, c01);
        double c1 = Lerp(tz, c10, c11);

        // Interpolate along y
        return Lerp(ty, c0, c1);
    }

    private static double Lerp(double t, double a, double b)
    {
        return a + t * (b - a);
    }



    private int NoiseToY(double noise) => noise > 0 ? (int)(positiveNoiseFactor * Math.Min(noise, 1.0)) : (int)(negativeNoiseFactor * Math.Max(noise, -1.0));

    private async ValueTask SetBlockAsync(Vector position, IBlock block, IChunk? chunk)
    {
        if (chunk is IChunk c && position.X >> 4 == c.X && position.Z >> 4 == c.Z)
        {
            c.SetBlock(position, block);
        }
        else
        {
            await world.SetBlockUntrackedAsync(position, block, false);
        }
    }

    private ValueTask SetBlockAsync(int x, int y, int z, IBlock block, IChunk? chunk) => SetBlockAsync(new Vector(x, y, z), block, chunk);

    private ValueTask SetBlockAsync(int x, int y, int z, IBlock block) => world.SetBlockUntrackedAsync(x, y, z, block, false);

    private ValueTask SetBlockAsync(Vector position, IBlock block) => world.SetBlockUntrackedAsync(position, block, false);

    private async ValueTask<IBlock?> GetBlockAsync(Vector position, IChunk? chunk)
    {
        if (chunk is IChunk c && position.X >> 4 == c.X && position.Z >> 4 == c.Z)
        {
            return c.GetBlock(position);
        }
        return await world.GetBlockAsync(position);
    }

    public ValueTask<IBlock?> GetBlockAsync(int x, int y, int z, IChunk? chunk) => GetBlockAsync(new Vector(x, y, z), chunk);

    public ValueTask<IBlock?> GetBlockAsync(int x, int y, int z) => world.GetBlockAsync(x, y, z);

    public ValueTask<IBlock?> GetBlockAsync(Vector position) => world.GetBlockAsync(position);
}
