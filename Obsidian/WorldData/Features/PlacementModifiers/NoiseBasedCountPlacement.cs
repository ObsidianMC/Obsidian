namespace Obsidian.WorldData.Features.PlacementModifiers;

/// <summary>
/// When the noise value at the current block position is positive, returns multiple copies of the current block position, 
/// whose count is based on a noise value and can gradually change based on the noise value. 
/// When noise value is negative or 0, returns empty. 
/// The count is calculated by ceil((noise(x / noise_factor, z / noise_factor) + noise_offset) * noise_to_count_ratio).
/// </summary>
[TreeProperty("minecraft:noise_based_count")]
public sealed class NoiseBasedCountPlacement : PlacementModifierBase
{
    public override string Type { get; internal init; } = "minecraft:noise_based_count";

    /// <summary>
    /// Scales the noise input horizontally. 
    /// Higher values make for wider and more spaced out peaks.
    /// </summary>
    public required double NoiseFactor { get; init; }

    /// <summary>
    /// Vertical offset of the noise.
    /// </summary>
    public double NoiseOffset { get; init; }

    /// <summary>
    /// Ratio of noise value to count.
    /// </summary>
    public required int NoiseToCountRatio { get; init; }

    protected override bool ShouldPlace(PlacementContext context) => throw new NotImplementedException();
}
