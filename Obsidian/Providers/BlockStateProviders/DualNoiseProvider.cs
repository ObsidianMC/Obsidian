namespace Obsidian.Providers.BlockStateProviders;

[ConfiguredFeatureProperty("minecraft:dual_noise_provider")]
public sealed class DualNoiseProvider : IBlockStateProvider
{
    public required string Type { get; init; } = "minecraft:dual_noise_provider";

    public required long Seed { get; set; }

    public SimpleNoise? Noise { get; set; }

    public SimpleNoise? SlowNoise { get; set; }

    public required float SlowScale { get; set; }

    public required float Scale { get; set; }

    public required int[] Variety { get; set; }

    public List<SimpleBlockState> States { get; set; } = [];

    //TODO
    public IBlock Get()
    {
        //BlocksRegistry.GetFromSimpleState(simpleState);
        return BlocksRegistry.Air;
    }

    public SimpleBlockState GetSimple()
    {
        return new() { Name = BlocksRegistry.Air.UnlocalizedName };
    }
}
