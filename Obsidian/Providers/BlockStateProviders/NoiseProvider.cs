﻿using Obsidian.Registries;

namespace Obsidian.Providers.BlockStateProviders;
public sealed class NoiseProvider : IBlockStateProvider
{
    public string Identifier => "minecraft:noise_provider";

    public required long Seed { get; set; }

    public SimpleNoise? Noise { get; set; }

    public required float Scale { get; set; }

    public List<SimpleBlockState> States { get; } = [];

    //TODO
    public IBlock Get()
    {
        return BlocksRegistry.Air;
    }

    public SimpleBlockState GetSimple()
    {
        return new() { Name = BlocksRegistry.Air.UnlocalizedName };
    }
}
