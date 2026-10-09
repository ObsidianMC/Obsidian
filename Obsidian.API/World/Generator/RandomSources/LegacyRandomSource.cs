namespace Obsidian.API.World.Generator.RandomSources;

/// <summary>
/// 48-bit linear congruential generator identical to <c>java.util.Random</c> (vanilla <c>LegacyRandomSource</c>).
/// Used by legacy noises, features and structure placement.
/// </summary>
public class LegacyRandomSource : BitRandomSource
{
    private JavaLcg state;

    public LegacyRandomSource(long seed) => this.state = new JavaLcg(seed);

    public override IRandomSource Fork() => new LegacyRandomSource(this.NextLong());

    public override IPositionalRandomFactory ForkPositional() => new LegacyPositionalRandomFactory(this.NextLong());

    public override void SetSeed(long seed)
    {
        this.state.SetSeed(seed);
        this.ResetGaussian();
    }

    public override int Next(int bits) => this.state.Next(bits);
}
