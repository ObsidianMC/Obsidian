namespace Obsidian.API.World.Generator.SurfaceConditions;
public sealed record class NoiseThresholdSurfaceCondition : ISurfaceCondition
{
    public string Type => "minecraft:noise_threshold";

    public string Noise { get; set; }

    public double MinThreshold { get; set; }

    public double MaxThreshold { get; set; }
}
