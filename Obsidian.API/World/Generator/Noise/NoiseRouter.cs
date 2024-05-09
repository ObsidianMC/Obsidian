namespace Obsidian.API.World.Generator.Noise;
public sealed class NoiseRouter
{
    public object Barrier { get; set; }

    public string Continents { get; set; }

    public string Depth { get; set; }

    public string Erosion { get; set; }

    public IDensityFunction FinalDensity { get; set; }

    public Noise FluidLevelFloodedness { get; set; }

    public Noise FLuidLevelSpread { get; set; }

    public IDensityFunction InitialDensityWithoutJaggedness { get; set; }

    public Noise Lava { get; set; }

    public string Ridges { get; set; }

    public IDensityFunction Temperature { get; set; }

    public IDensityFunction Vegetation { get; set; }

    public Noise VeinGap { get; set; }

    public IDensityFunction VeinRidged { get; set; }

    public IDensityFunction VeinToggle { get; set; }
}
