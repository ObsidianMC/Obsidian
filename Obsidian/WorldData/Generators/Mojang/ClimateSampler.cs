using Obsidian.API.World.Generator.Noise;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Samples the router's climate functions at quart positions, mirroring vanilla's Climate.Sampler.
/// </summary>
internal sealed class ClimateSampler
{
    private readonly IDensityFunction temperature;
    private readonly IDensityFunction humidity;
    private readonly IDensityFunction continentalness;
    private readonly IDensityFunction erosion;
    private readonly IDensityFunction depth;
    private readonly IDensityFunction weirdness;

    public ClimateSampler(IDensityFunction temperature, IDensityFunction humidity, IDensityFunction continentalness,
        IDensityFunction erosion, IDensityFunction depth, IDensityFunction weirdness)
    {
        this.temperature = temperature;
        this.humidity = humidity;
        this.continentalness = continentalness;
        this.erosion = erosion;
        this.depth = depth;
        this.weirdness = weirdness;
    }

    public ClimateSampler(NoiseRouter router)
        : this(router.Temperature, router.Vegetation, router.Continents, router.Erosion, router.Depth, router.Ridges)
    {
    }

    /// <summary>
    /// Samples climate at the block position of the given quart (4x4x4) coordinates.
    /// </summary>
    public TargetPoint Sample(int quartX, int quartY, int quartZ)
    {
        var x = quartX << 2;
        var y = quartY << 2;
        var z = quartZ << 2;

        // Narrowing to float before quantizing matches vanilla.
        return new TargetPoint(
            Climate.Quantize((float)this.temperature.GetValue(x, y, z)),
            Climate.Quantize((float)this.humidity.GetValue(x, y, z)),
            Climate.Quantize((float)this.continentalness.GetValue(x, y, z)),
            Climate.Quantize((float)this.erosion.GetValue(x, y, z)),
            Climate.Quantize((float)this.depth.GetValue(x, y, z)),
            Climate.Quantize((float)this.weirdness.GetValue(x, y, z)));
    }
}
