﻿using Obsidian.Registries;
using Obsidian.WorldData.Generators;
using Obsidian.WorldData.Generators.Overworld;

namespace Obsidian.WorldData.Decorators;

public abstract class BaseDecorator : IDecorator
{
    public DecoratorFeatures Features { get; }

    protected Biome biome;

    protected Chunk chunk;

    protected Vector pos;

    protected OverworldTerrainNoise noise;

    protected BaseDecorator(Biome biome, Chunk chunk, Vector pos, GenHelper helper)
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
        if (chunk is null) { return; }

        if (pos.Y <= noise.WaterLevel)
        {
            chunk.SetBlock(pos, BlocksRegistry.Sand);
        }
    }
}
