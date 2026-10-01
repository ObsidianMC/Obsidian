namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Caches the last value per block column.
/// </summary>
/// <remarks>
/// Only a marker: evaluating it directly returns the argument. Chunk generation swaps it for a
/// chunk-bound implementation through <see cref="IDensityFunction.MapAll"/>.
/// </remarks>
[DensityFunction("minecraft:cache_2d")]
public sealed class Cache2DDensityFunction : IDensityFunction
{
    public string Type => "minecraft:cache_2d";

    public required IDensityFunction Argument { get; init; }

    public double MinValue => this.Argument.MinValue;

    public double MaxValue => this.Argument.MaxValue;

    public double GetValue(double x, double y, double z) => this.Argument.GetValue(x, y, z);

    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new Cache2DDensityFunction { Argument = visitor.Map(this.Argument) });
}
