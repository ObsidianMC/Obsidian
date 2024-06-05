﻿namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:cache_once")]
public sealed class CacheOnceDensityFunction : IDensityFunction
{
    public string Type => "minecraft:cache_once";

    public required IDensityFunction Argument { get; init; }

    public double GetValue(double x, double y, double z) => throw new NotImplementedException();
}
