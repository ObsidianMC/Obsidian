namespace Obsidian.API.World.Generator.SurfaceConditions;

/// <summary>
/// Tests whether a registered noise sample is within the configured thresholds.
/// </summary>
[SurfaceCondition("minecraft:noise_threshold")]
public sealed record class NoiseThresholdSurfaceCondition : ISurfaceCondition
{
    /// <summary>
    /// Gets the surface condition codec type identifier.
    /// </summary>
    public string Type => "minecraft:noise_threshold";

    /// <summary>
    /// Gets the registered noise whose identifier the surface evaluator resolves for the world.
    /// </summary>
    public required INoise Noise { get; init; }

    /// <summary>
    /// Gets the inclusive lower noise threshold, read from <c>min_threshold</c>.
    /// </summary>
    public required double MinThreshold { get; init; }

    /// <summary>
    /// Gets the inclusive upper noise threshold, read from <c>max_threshold</c>.
    /// </summary>
    public required double MaxThreshold { get; init; }
}
