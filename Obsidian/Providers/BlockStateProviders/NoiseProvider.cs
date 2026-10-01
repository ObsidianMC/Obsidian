using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.Providers.BlockStateProviders;

/// <summary>
/// Picks a state from a list by sampling a fixed-seed noise at the position.
/// </summary>
[ConfiguredFeatureProperty("minecraft:noise_provider")]
public class NoiseProvider : IBlockStateProvider
{
    public virtual string Type { get; init; } = "minecraft:noise_provider";

    public required long Seed { get; init; }

    public required NoiseParameters Noise { get; init; }

    public required float Scale { get; init; }

    public required SimpleBlockState[] States { get; init; }

    protected IBlock[] Blocks => field ??= Array.ConvertAll(this.States, BlocksRegistry.GetFromSimpleState);

    private API.Noise.NormalNoise NoiseInstance => field ??= this.Noise.Create(this.Seed);

    public virtual IBlock GetState(IRandomSource random, Vector position) => this.GetRandomState(this.Blocks, position, this.Scale);

    protected IBlock GetRandomState(IReadOnlyList<IBlock> states, Vector position, double scale) =>
        GetRandomState(states, this.GetNoiseValue(position, scale));

    protected double GetNoiseValue(Vector position, double scale) =>
        this.NoiseInstance.GetValue(position.X * scale, position.Y * scale, position.Z * scale);

    protected static IBlock GetRandomState(IReadOnlyList<IBlock> states, double noise)
    {
        var value = Math.Clamp((1.0 + noise) / 2.0, 0.0, 0.9999);
        return states[(int)(value * states.Count)];
    }
}
