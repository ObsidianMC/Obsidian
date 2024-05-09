namespace Obsidian.API.World.Generator.Noise;
public readonly struct ClimateParameter
{
    public double[] Continentalness { get; init; }

    public double Depth { get; init; }

    public double[] Erosion { get; init; }

    public double[] Humidity { get; init; }

    public double Offset { get; init; }

    public double[] Temperature { get; init; }

    public double[] Weirdness { get; init; }
}
