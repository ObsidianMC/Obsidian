namespace Obsidian.API.World.Generator.RandomSources;

/// <summary>
/// Positional factory produced by <see cref="XoroshiroRandomSource.ForkPositional"/>.
/// </summary>
public sealed class XoroshiroPositionalRandomFactory : IPositionalRandomFactory
{
    private readonly long seedLo;
    private readonly long seedHi;

    /// <summary>
    /// Stores the two seed halves used to derive positional and named xoroshiro random sources.
    /// </summary>
    public XoroshiroPositionalRandomFactory(long seedLo, long seedHi)
    {
        this.seedLo = seedLo;
        this.seedHi = seedHi;
    }

    /// <summary>
    /// Creates a repeatable random source from the coordinates and this factory's seed.
    /// </summary>
    public IRandomSource At(int x, int y, int z) => new XoroshiroRandomSource(Mth.GetSeed(x, y, z) ^ this.seedLo, this.seedHi);

    /// <summary>
    /// The state of the random <see cref="At"/> creates, without the allocation.
    /// </summary>
    internal Xoroshiro128PlusPlus StateAt(int x, int y, int z) => new(Mth.GetSeed(x, y, z) ^ this.seedLo, this.seedHi);

    /// <summary>
    /// Creates a repeatable random source by combining the name's hash with this factory's seed.
    /// </summary>
    public IRandomSource FromHashOf(string name) =>
        new XoroshiroRandomSource(RandomSupport.SeedFromHashOf(name).Xor(this.seedLo, this.seedHi));

    /// <summary>
    /// Creates a xoroshiro source with the supplied seed XORed into each factory seed half.
    /// </summary>
    public IRandomSource FromSeed(long seed) => new XoroshiroRandomSource(seed ^ this.seedLo, seed ^ this.seedHi);
}
