namespace Obsidian.API.World.Generator.Noise;
public sealed class NoiseRouter
{
    public IDensityFunction Barrier { get; set; }

    public IDensityFunction Continents { get; set; }

    public IDensityFunction Depth { get; set; }

    public IDensityFunction Erosion { get; set; }

    public IDensityFunction FinalDensity { get; set; }

    public IDensityFunction FluidLevelFloodedness { get; set; }

    public IDensityFunction FluidLevelSpread { get; set; }

    public IDensityFunction InitialDensityWithoutJaggedness { get; set; }

    public IDensityFunction Lava { get; set; }

    public IDensityFunction Ridges { get; set; }

    public IDensityFunction Temperature { get; set; }

    public IDensityFunction Vegetation { get; set; }

    public IDensityFunction VeinGap { get; set; }

    public IDensityFunction VeinRidged { get; set; }

    public IDensityFunction VeinToggle { get; set; }
}
