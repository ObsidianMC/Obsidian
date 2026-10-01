using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.Providers.BlockStateProviders;

/// <summary>
/// Like <see cref="NoiseProvider"/>, but a second, slower noise first narrows the list to a varying number of states.
/// </summary>
[ConfiguredFeatureProperty("minecraft:dual_noise_provider")]
public sealed class DualNoiseProvider : NoiseProvider
{
    public override string Type { get; init; } = "minecraft:dual_noise_provider";

    /// <summary>
    /// Inclusive range <c>[min, max]</c> of how many states to choose from.
    /// </summary>
    public required int[] Variety { get; init; }

    public required NoiseParameters SlowNoise { get; init; }

    public required float SlowScale { get; init; }

    private API.Noise.NormalNoise SlowNoiseInstance => field ??= this.SlowNoise.Create(this.Seed);

    public override IBlock GetState(IRandomSource random, Vector position)
    {
        var slow = this.GetSlowNoiseValue(position);
        var count = (int)ClampedMap(slow, -1.0, 1.0, this.Variety[0], this.Variety[1] + 1);

        var candidates = new List<IBlock>(count);
        for (var i = 0; i < count; i++)
            candidates.Add(GetRandomState(this.Blocks, this.GetSlowNoiseValue(position + new Vector(i * 54545, 0, i * 34234))));

        return this.GetRandomState(candidates, position, this.Scale);
    }

    private double GetSlowNoiseValue(Vector position) =>
        this.SlowNoiseInstance.GetValue(position.X * this.SlowScale, position.Y * this.SlowScale, position.Z * this.SlowScale);

    private static double ClampedMap(double value, double fromMin, double fromMax, double toMin, double toMax)
    {
        var delta = (value - fromMin) / (fromMax - fromMin);
        return delta < 0.0 ? toMin : delta > 1.0 ? toMax : toMin + delta * (toMax - toMin);
    }
}
