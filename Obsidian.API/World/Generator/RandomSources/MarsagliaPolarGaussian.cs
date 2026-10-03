namespace Obsidian.API.World.Generator.RandomSources;

/// <summary>
/// Marsaglia polar method gaussian generator that caches the second value of each pair.
/// Mutable struct: keep it in a non-readonly field.
/// </summary>
internal struct MarsagliaPolarGaussian
{
    private double nextNextGaussian;
    private bool haveNextNextGaussian;

    public void Reset() => this.haveNextNextGaussian = false;

    public double Next(IRandomSource random)
    {
        if (this.haveNextNextGaussian)
        {
            this.haveNextNextGaussian = false;
            return this.nextNextGaussian;
        }

        double x, y, lengthSquared;
        do
        {
            x = 2.0 * random.NextDouble() - 1.0;
            y = 2.0 * random.NextDouble() - 1.0;
            lengthSquared = x * x + y * y;
        }
        while (lengthSquared >= 1.0 || lengthSquared == 0.0);

        var multiplier = Math.Sqrt(-2.0 * Math.Log(lengthSquared) / lengthSquared);
        this.nextNextGaussian = y * multiplier;
        this.haveNextNextGaussian = true;
        return x * multiplier;
    }
}
