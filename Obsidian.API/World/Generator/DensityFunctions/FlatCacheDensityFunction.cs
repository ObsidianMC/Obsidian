namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:flat_cache")]
public sealed class FlatCacheDensityFunction : IDensityFunction
{
    private double? cachedValue;
    public string Type => "minecraft:flat_cache";

    public required IDensityFunction Argument { get; init; }

    public double MinValue => Argument.MinValue;

    public double MaxValue => Argument.MaxValue;

    public double GetValue(double x, double y, double z)
    {
        return this.Argument.GetValue(x, y, z);
        // cache needs to invalidate when x/y/z delta > 4
        if (x % 4 == 0 && z % 4 == 0)
        {
            if (y == 0 && this.cachedValue.HasValue)
                return this.cachedValue.Value;

            this.cachedValue = this.Argument.GetValue(x, y, z);

            return this.cachedValue.Value;
        }

        this.cachedValue ??= this.Argument.GetValue(x, y, z);

        return this.cachedValue.Value;
    }
}
