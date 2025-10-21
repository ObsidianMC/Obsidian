using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.WorldData.Generators;
using Obsidian.WorldData.Generators.Overworld;

namespace Obsidian.WorldData.Decorators;

public abstract class BaseDecorator(BiomeCodec biome, IChunk chunk, Vector pos, GenHelper helper) : IDecorator
{
    public DecoratorFeatures Features { get; } = new DecoratorFeatures();

    protected BiomeCodec Biome { get; } = biome;

    protected IChunk Chunk { get; } = chunk;

    protected Vector Position { get; } = pos;

    protected OverworldTerrainNoise Noise { get; } = helper.Noise;

    public abstract void Decorate();

    protected void FillWater()
    {
/*        if (chunk is null) { return; }

        if (pos.Y <= noise.waterLevel)
        {
            chunk.SetBlock(pos, BlocksRegistry.Sand);
            for (int y = noise.waterLevel; y > pos.Y; y--)
            {
                chunk.SetBlock(pos.X, y, pos.Z, BlocksRegistry.Water);
            }
        }*/
    }

    protected void FillSand()
    {
        if (Chunk is null) { return; }

        if (Position.Y <= Noise.WaterLevel)
        {
            Chunk.SetBlock(Position, BlocksRegistry.Sand);
        }
    }
}
