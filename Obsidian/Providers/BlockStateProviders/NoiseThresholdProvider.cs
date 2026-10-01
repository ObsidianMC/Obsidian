using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.Providers.BlockStateProviders;

/// <summary>
/// Below a noise threshold picks from the low states; above it, picks from the high states with
/// <c>HighChance</c>, otherwise the default state.
/// </summary>
[ConfiguredFeatureProperty("minecraft:noise_threshold_provider")]
public sealed class NoiseThresholdProvider : IBlockStateProvider
{
    public string Type { get; init; } = "minecraft:noise_threshold_provider";

    public required long Seed { get; init; }

    public required NoiseParameters Noise { get; init; }

    public required float Scale { get; init; }

    public required float Threshold { get; init; }

    public required float HighChance { get; init; }

    public required SimpleBlockState DefaultState { get; init; }

    public required SimpleBlockState[] LowStates { get; init; }

    public required SimpleBlockState[] HighStates { get; init; }

    private API.Noise.NormalNoise NoiseInstance => field ??= this.Noise.Create(this.Seed);

    private IBlock DefaultBlock => field ??= BlocksRegistry.GetFromSimpleState(this.DefaultState);

    private IBlock[] LowBlocks => field ??= Array.ConvertAll(this.LowStates, BlocksRegistry.GetFromSimpleState);

    private IBlock[] HighBlocks => field ??= Array.ConvertAll(this.HighStates, BlocksRegistry.GetFromSimpleState);

    public IBlock GetState(IRandomSource random, Vector position)
    {
        var noise = this.NoiseInstance.GetValue(position.X * this.Scale, position.Y * this.Scale, position.Z * this.Scale);

        if (noise < this.Threshold)
            return this.LowBlocks[random.NextInt(this.LowBlocks.Length)];

        return random.NextFloat() < this.HighChance ? this.HighBlocks[random.NextInt(this.HighBlocks.Length)] : this.DefaultBlock;
    }
}
