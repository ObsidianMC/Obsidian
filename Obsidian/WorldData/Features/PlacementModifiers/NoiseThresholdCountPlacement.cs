using Obsidian.WorldData.Generators.Mojang;

using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.PlacementModifiers;

/// <summary>
/// Repeats the position <see cref="BelowNoise"/> or <see cref="AboveNoise"/> times depending on the biome info noise.
/// </summary>
[ConfiguredFeatureProperty("minecraft:noise_threshold_count")]
public sealed class NoiseThresholdCountPlacement : PlacementModifierBase
{
    public override string Type => "minecraft:noise_threshold_count";

    public required double NoiseLevel { get; init; }

    public required int BelowNoise { get; init; }

    public required int AboveNoise { get; init; }

    public override IEnumerable<Vector> GetPositions(PlacementContext context, IRandomSource random, Vector position)
    {
        var noise = BiomeTemperature.BiomeInfoNoise.GetValue(position.X / 200.0, position.Z / 200.0, false);
        return Enumerable.Repeat(position, noise < this.NoiseLevel ? this.BelowNoise : this.AboveNoise);
    }
}
