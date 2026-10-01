namespace Obsidian.API.World.Generator.RandomSources;

/// <summary>
/// Positional factory produced by <see cref="XoroshiroRandomSource.ForkPositional"/>.
/// </summary>
public sealed class XoroshiroPositionalRandomFactory : IPositionalRandomFactory
{
    private readonly long seedLo;
    private readonly long seedHi;

    public XoroshiroPositionalRandomFactory(long seedLo, long seedHi)
    {
        this.seedLo = seedLo;
        this.seedHi = seedHi;
    }

    public IRandomSource At(int x, int y, int z) => new XoroshiroRandomSource(Mth.GetSeed(x, y, z) ^ this.seedLo, this.seedHi);

    public IRandomSource FromHashOf(string name) =>
        new XoroshiroRandomSource(RandomSupport.SeedFromHashOf(name).Xor(this.seedLo, this.seedHi));

    public IRandomSource FromSeed(long seed) => new XoroshiroRandomSource(seed ^ this.seedLo, seed ^ this.seedHi);
}
