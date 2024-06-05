namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:weird_scaled_sampler")]
public sealed class WeirdScaledSamplerDensityFunction : IDensityFunction
{
    public string Type => "minecraft:weird_scaled_sampler";

    public required IDensityFunction Input { get; init; }

    public double GetValue(double x, double y, double z) => throw new NotImplementedException();
}
