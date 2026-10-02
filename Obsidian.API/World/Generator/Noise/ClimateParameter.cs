namespace Obsidian.API.World.Generator.Noise;
public readonly record struct ClimateParameter
{
    public ImmutableArray<double> Continentalness { get; init; }

    public double Depth { get; init; }

    public ImmutableArray<double> Erosion { get; init; }

    public ImmutableArray<double> Humidity { get; init; }

    public double Offset { get; init; }

    public ImmutableArray<double> Temperature { get; init; }

    public ImmutableArray<double> Weirdness { get; init; }
}
