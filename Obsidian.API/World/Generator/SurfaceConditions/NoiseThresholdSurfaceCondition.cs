namespace Obsidian.API.World.Generator.SurfaceConditions;

[SurfaceCondition("minecraft:noise_threshold")]
public sealed record class NoiseThresholdSurfaceCondition : ISurfaceCondition
{
    public string Type => "minecraft:noise_threshold";

    public required INoise Noise { get; init; }

    public required double MinThreshold { get; init; }

    public required double MaxThreshold { get; init; }
}
