namespace Obsidian.API.World.Generator.RandomSources;

/// <summary>
/// Port of vanilla's <c>PositionalRandomFactory</c>: derives independent, deterministic randoms
/// from a block position, a name (e.g. <c>"minecraft:terrain"</c>) or a seed.
/// </summary>
public interface IPositionalRandomFactory
{
    /// <summary>Creates a random for the block at the given position.</summary>
    IRandomSource At(int x, int y, int z);

    /// <summary>Creates a random keyed by <paramref name="name"/>, usually a resource location string.</summary>
    IRandomSource FromHashOf(string name);

    /// <summary>Creates a random from <paramref name="seed"/>.</summary>
    IRandomSource FromSeed(long seed);
}
