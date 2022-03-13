using Obsidian.Utilities.Registry;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public abstract class BaseDecorator : IDecorator
{
    public DecoratorFeatures Features { get; }

    protected Biomes biome;

    protected Chunk chunk;

    protected Vector pos;

    protected OverworldTerrainNoise noise;

    protected BaseDecorator(Biomes biome, Chunk chunk, Vector pos, GenHelper helper)
    {
        this.biome = biome;
        this.chunk = chunk;
        this.pos = pos;
        this.noise = helper.Noise;

        Features = new DecoratorFeatures();
    }

    public abstract void Decorate();

    protected void FillWater()
    {
        if (chunk is null) { return; }
        var water = Registry.GetBlock(Material.Water);
        var sand = Registry.GetBlock(Material.Sand);
        if (pos.Y <= noise.Settings.WaterLevel)
        {
            chunk.SetBlock(pos, sand);
            for (int y = noise.Settings.WaterLevel; y > pos.Y; y--)
            {
                chunk.SetBlock(pos.X, y, pos.Z, water);
            }
        }
    }

    protected void FillSand()
    {
        if (chunk is null) { return; }
        var sand = Registry.GetBlock(Material.Sand);
        if (pos.Y <= noise.Settings.WaterLevel)
        {
            chunk.SetBlock(pos, sand);
        }
    }
}
