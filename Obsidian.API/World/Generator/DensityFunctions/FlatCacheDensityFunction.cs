namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Caches the argument per quart column (sampled at y = 0) for the chunk being generated.
/// </summary>
/// <remarks>
/// Only a marker: evaluating it directly returns the argument. Chunk generation swaps it for a
/// chunk-bound implementation through <see cref="IDensityFunction.MapAll"/>.
/// </remarks>
[DensityFunction("minecraft:flat_cache")]
public sealed class FlatCacheDensityFunction : IDensityFunction
{
    public string Type => "minecraft:flat_cache";

    public required IDensityFunction Argument { get; init; }

    public double MinValue => this.Argument.MinValue;

    public double MaxValue => this.Argument.MaxValue;

    public double GetValue(double x, double y, double z) => this.Argument.GetValue(x, y, z);

    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new FlatCacheDensityFunction { Argument = visitor.Map(this.Argument) });
}
