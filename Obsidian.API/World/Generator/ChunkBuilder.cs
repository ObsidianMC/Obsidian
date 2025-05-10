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
        Settings = NoiseRegistry.NoiseSettings.All[noiseSettingTag];

        positiveNoiseFactor = Settings.Noise.Height + Settings.Noise.MinY - Settings.SeaLevel;
        negativeNoiseFactor = Settings.Noise.Height - positiveNoiseFactor - Settings.SeaLevel;

        var aquiferBarrierNoise = Settings.NoiseRouter.Barrier;
        var aquiferFluidLevelFloodednessNoise = Settings.NoiseRouter.FluidLevelFloodedness;
        var aquiferFluidLevelSpreadNoise = Settings.NoiseRouter.FluidLevelSpread;
        var temperatureNoise = Settings.NoiseRouter.Temperature;
        var vegetationNoise = Settings.NoiseRouter.Vegetation;
        var terrainFactor = NoiseRegistry.DensityFunctions.Overworld.Factor;
        var terrainDepth = NoiseRegistry.DensityFunctions.Overworld.Depth;
        baseDensity = NoiseGradientDensity(new Cache2DDensityFunction() { Argument = terrainFactor }, terrainDepth);
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
                var val = baseDensity.GetValue(((chunk.X << 4) + x)*4, 0, ((chunk.Z << 4) + z)*4);
                val = (val - 5.0) / 5.0;
                int y = NoiseToY(val);
                chunk.SetBlock(x, y, z, fill);

            }
        }
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
