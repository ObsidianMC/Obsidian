using Obsidian.API.Noise;

namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:y_clamped_gradient")]
public sealed class YClampedGradientDensityFunction : IDensityFunction
{
    public required double FromValue { get; init; }
    public required double FromY { get; init; }
    public required double ToValue { get; init; }
    public required double ToY { get; init; }

    public string Type => "minecraft:y_clamped_gradient";

    public double MinValue => Math.Min(FromValue, ToValue);

    public double MaxValue => Math.Max(FromValue, ToValue);

    public double GetValue(double x, double y, double z) => MathUtils.ClampedMap(y, FromY, ToY, FromValue, ToValue);
}
