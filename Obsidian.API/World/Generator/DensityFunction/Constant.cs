﻿namespace Obsidian.API.World.Generator.DensityFunction;
public sealed class Constant : IDensityFunction
{
    public string Type => "minecraft:constant";

    public required double Argument { get; init; }

    public double GetValue(double x, double y, double z) => Argument;
}
