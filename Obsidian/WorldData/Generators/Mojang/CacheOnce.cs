namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Remembers the value of the last sampled position, like vanilla's cache once. Density functions are pure, so this only
/// saves work. Not thread-safe.
/// </summary>
internal sealed class CacheOnce : IDensityFunction, IChunkCache
{
    private readonly IDensityFunction argument;
    private double lastX = double.NaN;
    private double lastY;
    private double lastZ;
    private double lastValue;

    public string Type => "minecraft:cache_once";

    public double MinValue => this.argument.MinValue;

    public double MaxValue => this.argument.MaxValue;

    public IDensityFunction Argument => this.argument;

    public CacheOnce(IDensityFunction argument) => this.argument = argument;

    public void Reset() => this.lastX = double.NaN;

    public double GetValue(double x, double y, double z)
    {
        if (x == this.lastX && y == this.lastY && z == this.lastZ)
            return this.lastValue;

        this.lastX = x;
        this.lastY = y;
        this.lastZ = z;
        this.lastValue = this.argument.GetValue(x, y, z);
        return this.lastValue;
    }
}
