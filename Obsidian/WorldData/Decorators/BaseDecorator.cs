using Obsidian.WorldData.Generators;
using Obsidian.WorldData.Generators.Overworld;

namespace Obsidian.WorldData.Decorators;

public abstract class BaseDecorator : IDecorator
{
    public DecoratorFeatures Features { get; }

    protected Biome Biome { get; }

    protected IChunk Chunk { get; }

    protected Vector Position { get; }

    protected OverworldTerrainNoise Noise { get; }

    protected BaseDecorator(Biome biome, IChunk chunk, Vector pos, GenHelper helper)
    {
        this.Biome = biome;
        this.Chunk = chunk;
        this.Position = pos;
        this.Noise = helper.Noise;

        Features = new DecoratorFeatures();
    }

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
