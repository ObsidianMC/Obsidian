namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:invert")]
public sealed class InvertDensityFunction : IDensityFunction
{
    public string Type => "minecraft:invert";

    public required IDensityFunction Argument { get; init; }

    // When the argument's range straddles zero the reciprocal is unbounded in both directions.
    public double MinValue => StraddlesZero ? double.NegativeInfinity : 1.0 / Argument.MaxValue;

    public double MaxValue => StraddlesZero ? double.PositiveInfinity : 1.0 / Argument.MinValue;

    private bool StraddlesZero => Argument.MinValue < 0.0 && Argument.MaxValue > 0.0;

    public double GetValue(double x, double y, double z) => 1.0 / Argument.GetValue(x, y, z);
}
