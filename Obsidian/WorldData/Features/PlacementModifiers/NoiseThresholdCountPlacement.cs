namespace Obsidian.WorldData.Features.PlacementModifiers;

/// <summary>
/// Returns multiple copies of the current block position. 
/// The count is either below_noise or above_noise, based on the noise value at the current block position. 
/// First checks noise(x / 200, z / 200) less than noise_level
/// If that is true, uses below_noise, otherwise above_noise.
/// </summary>
[TreeProperty("minecraft:noise_threshold_count")]
public sealed class NoiseThresholdCountPlacement : PlacementModifierBase
{
    public override string Type { get; internal init; } = "minecraft:noise_threshold_count";

    /// <summary>
    /// The threshold within the noise of when to use below_noise or above_noise.
    /// </summary>
    public required double NoiseLevel { get; init; }

    /// <summary>
    /// The count when the noise is below the threshold. 
    /// Value lower than 0 is treated as 0.
    /// </summary>
    public required int BelowNoise { get; init; }

    /// <summary>
    /// The count when the noise is above the threshold. 
    /// Value lower than 0 is treated as 0.
    /// </summary>
    public required int AboveNoise { get; init; }

    protected override bool ShouldPlace(PlacementContext context) => throw new NotImplementedException();
}
