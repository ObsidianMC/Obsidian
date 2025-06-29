namespace Obsidian.API.World.Generator.Noise;
public sealed class NoiseRouter
{
    public required IDensityFunction Barrier { get; set; }

    public required IDensityFunction Continents { get; set; }

    public required IDensityFunction Depth { get; set; }

    public required IDensityFunction Erosion { get; set; }

    public required IDensityFunction FinalDensity { get; set; }

    public required IDensityFunction FluidLevelFloodedness { get; set; }

    public required IDensityFunction FluidLevelSpread { get; set; }

    public required IDensityFunction InitialDensityWithoutJaggedness { get; set; }

    public required IDensityFunction Lava { get; set; }

    public required IDensityFunction Ridges { get; set; }

    public required IDensityFunction Temperature { get; set; }

    public required IDensityFunction Vegetation { get; set; }

    public required IDensityFunction VeinGap { get; set; }

    public required IDensityFunction VeinRidged { get; set; }

    public required IDensityFunction VeinToggle { get; set; }
}
