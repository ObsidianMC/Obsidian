namespace Obsidian.API.World.Generator.RandomSources;

/// <summary>
/// Wraps another source and adds vanilla's feature/decoration seeding helpers (vanilla <c>WorldgenRandom</c>).
/// All values are derived from the wrapped source: a <see cref="LegacyRandomSource"/> supplies bits directly,
/// any other source supplies the top bits of <see cref="IRandomSource.NextLong"/>.
/// </summary>
public sealed class WorldgenRandom : LegacyRandomSource
{
    private readonly IRandomSource randomSource;

    public WorldgenRandom(IRandomSource randomSource) : base(0L) => this.randomSource = randomSource;

    /// <summary>Number of times <see cref="Next"/> has been called.</summary>
    public int Count { get; private set; }

    public override IRandomSource Fork() => this.randomSource.Fork();

    public override IPositionalRandomFactory ForkPositional() => this.randomSource.ForkPositional();

    public override int Next(int bits)
    {
        this.Count++;
        return this.randomSource is LegacyRandomSource legacy
            ? legacy.Next(bits)
            : unchecked((int)(this.randomSource.NextLong() >>> (64 - bits)));
    }

    /// <remarks>
    /// Only reseeds the wrapped source. Like vanilla, this does not discard a cached gaussian of this wrapper.
    /// </remarks>
    public override void SetSeed(long seed) => this.randomSource.SetSeed(seed);

    /// <summary>Seeds for decorating the chunk whose minimum block corner is (<paramref name="minBlockX"/>, <paramref name="minBlockZ"/>).</summary>
    /// <returns>The decoration seed, to be passed to <see cref="SetFeatureSeed"/>.</returns>
    public long SetDecorationSeed(long levelSeed, int minBlockX, int minBlockZ)
    {
        this.SetSeed(levelSeed);
        var xMultiplier = this.NextLong() | 1L;
        var zMultiplier = this.NextLong() | 1L;
        var seed = unchecked(minBlockX * xMultiplier + minBlockZ * zMultiplier) ^ levelSeed;
        this.SetSeed(seed);
        return seed;
    }

    /// <summary>Seeds for the feature at <paramref name="index"/> within generation <paramref name="step"/>.</summary>
    public void SetFeatureSeed(long decorationSeed, int index, int step) =>
        this.SetSeed(unchecked(decorationSeed + index + 10000 * step));

    /// <summary>Seeds for carvers and legacy large features of the given chunk.</summary>
    public void SetLargeFeatureSeed(long levelSeed, int chunkX, int chunkZ)
    {
        this.SetSeed(levelSeed);
        var xMultiplier = this.NextLong();
        var zMultiplier = this.NextLong();
        this.SetSeed(unchecked(chunkX * xMultiplier) ^ unchecked(chunkZ * zMultiplier) ^ levelSeed);
    }

    /// <summary>Seeds for structure placement in the given region.</summary>
    public void SetLargeFeatureWithSalt(long levelSeed, int regionX, int regionZ, int salt) =>
        this.SetSeed(unchecked(regionX * 341873128712L + regionZ * 132897987541L + levelSeed + salt));
}
