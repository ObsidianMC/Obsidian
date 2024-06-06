﻿namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:noise")]
public class NoiseDensityFunction : IDensityFunction
{
    public virtual string Type => "minecraft:noise";

    public required INoise Noise { get; init; }

    public required double XzScale { get; set; }

    public required double YScale { get; set; }

    public virtual double GetValue(double x, double y, double z) => Noise.GetValue(x * XzScale, y * YScale, z * XzScale);
}
