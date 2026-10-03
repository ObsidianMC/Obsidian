namespace Obsidian.API.World.Generator.RandomSources;

/// <summary>
/// 48-bit linear congruential generator identical to <c>java.util.Random</c> (vanilla <c>LegacyRandomSource</c>).
/// Used by legacy noises, features and structure placement.
/// </summary>
public class LegacyRandomSource : BitRandomSource
{
    private const long Multiplier = 0x5DEECE66DL;
    private const long Increment = 0xBL;
    private const long ModulusMask = (1L << 48) - 1;

    private long seed;

    public LegacyRandomSource(long seed) => this.seed = Scramble(seed);

    public override IRandomSource Fork() => new LegacyRandomSource(this.NextLong());

    public override IPositionalRandomFactory ForkPositional() => new LegacyPositionalRandomFactory(this.NextLong());

    public override void SetSeed(long seed)
    {
        this.seed = Scramble(seed);
        this.ResetGaussian();
    }

    public override int Next(int bits)
    {
        this.seed = unchecked(this.seed * Multiplier + Increment) & ModulusMask;
        return unchecked((int)(this.seed >> (48 - bits)));
    }

    private static long Scramble(long seed) => (seed ^ Multiplier) & ModulusMask;
}
