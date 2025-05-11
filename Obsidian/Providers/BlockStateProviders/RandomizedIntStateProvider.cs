using Obsidian.Providers.IntProviders;

namespace Obsidian.Providers.BlockStateProviders;

[TreeProperty("minecraft:randomized_int_state_provider")]
public sealed class RandomizedIntStateProvider : IBlockStateProvider
{
    public string Type { get; init; } = "minecraft:randomized_int_state_provider";

    public string Property { get; set; } = default!;

    public IIntProvider Values { get; set; } = default!;

    public IBlockStateProvider Source { get; set; } = default!;

    public IBlock Get()
    {
        var simpleState = this.Source.GetSimple();
        if (this.Values is RangedIntProvider rangedIntProvider)
        {

            simpleState.Properties[this.Property] = rangedIntProvider.Type == IntProviderTypes.Uniform
                ? Globals.Random.Next(rangedIntProvider.MinInclusive, rangedIntProvider.MaxInclusive).ToString()
                : Math.Min(Globals.Random.Next(rangedIntProvider.MinInclusive, rangedIntProvider.MaxInclusive), rangedIntProvider.MinInclusive).ToString();//Not sure if this is right
        }

        return BlocksRegistry.GetFromSimpleState(simpleState);
    }

    public SimpleBlockState GetSimple()
    {
        var simpleState = this.Source.GetSimple();
        if (this.Values is RangedIntProvider rangedIntProvider)
        {
            simpleState.Properties[this.Property] = rangedIntProvider.Type == IntProviderTypes.Uniform
                ? Globals.Random.Next(rangedIntProvider.MinInclusive, rangedIntProvider.MaxInclusive).ToString()
                : Math.Min(Globals.Random.Next(rangedIntProvider.MinInclusive, rangedIntProvider.MaxInclusive), rangedIntProvider.MinInclusive).ToString();
        }

        return simpleState;
    }
}
