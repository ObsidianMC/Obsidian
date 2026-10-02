namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Remembers the value of the last sampled column (X and Z), for functions whose value doesn't depend on Y (see
/// <see cref="RandomState.IsYIndependent"/>). Not thread-safe.
/// </summary>
internal sealed class ColumnCache : IDensityFunction
{
    private readonly IDensityFunction argument;
    private double lastX = double.NaN;
    private double lastZ;
    private double lastValue;

    public string Type => "minecraft:cache_2d";

    public double MinValue => this.argument.MinValue;

    public double MaxValue => this.argument.MaxValue;

    public ColumnCache(IDensityFunction argument) => this.argument = argument;

    public double GetValue(double x, double y, double z)
    {
        if (x == this.lastX && z == this.lastZ)
            return this.lastValue;

        this.lastX = x;
        this.lastZ = z;
        this.lastValue = this.argument.GetValue(x, y, z);
        return this.lastValue;
    }
}
