namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:square")]
public sealed class SquareDensityFunction : IDensityFunction
{
    public string Type => "minecraft:square";

    public required IDensityFunction Argument { get; init; }

    public double MinValue => Math.Pow(Argument.MinValue, 2);

    public double MaxValue => Math.Pow(Argument.MaxValue, 2);

    public double GetValue(double x, double y, double z) => (float)Math.Pow(Argument.GetValue(x, y, z), 2);
}
