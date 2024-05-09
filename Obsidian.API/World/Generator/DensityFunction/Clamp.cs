﻿namespace Obsidian.API.World.Generator.DensityFunction;

[DensityFunction("minecraft:clamp")]
public sealed class Clamp : IDensityFunction
{
    public string Type => "minecraft:clamp";

    public required IDensityFunction Input { get; init; }

    public required double Min { get; init; }
    public required double Max { get; init; }

    public double GetValue(double x, double y, double z) => Math.Clamp(Input.GetValue(x, y, z), Min, Max);
}