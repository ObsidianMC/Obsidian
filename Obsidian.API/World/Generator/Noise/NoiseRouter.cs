namespace Obsidian.API.World.Generator.Noise;
/// <summary>
/// Groups the density functions used for terrain, climate, aquifers and ore veins.
/// </summary>
public sealed class NoiseRouter
{
    /// <summary>
    /// Gets or sets the aquifer barrier density, read from <c>barrier</c>.
    /// </summary>
    public required IDensityFunction Barrier { get; set; }

    /// <summary>
    /// Gets or sets the continentalness climate function, read from <c>continents</c>.
    /// </summary>
    public required IDensityFunction Continents { get; set; }

    /// <summary>
    /// Gets or sets the depth climate function, read from <c>depth</c>.
    /// </summary>
    public required IDensityFunction Depth { get; set; }

    /// <summary>
    /// Gets or sets the erosion climate function, read from <c>erosion</c>.
    /// </summary>
    public required IDensityFunction Erosion { get; set; }

    /// <summary>
    /// Gets or sets the final terrain density, read from <c>final_density</c>.
    /// </summary>
    public required IDensityFunction FinalDensity { get; set; }

    /// <summary>
    /// Gets or sets the aquifer floodedness function, read from <c>fluid_level_floodedness</c>.
    /// </summary>
    public required IDensityFunction FluidLevelFloodedness { get; set; }

    /// <summary>
    /// Gets or sets the aquifer fluid-height spread function, read from <c>fluid_level_spread</c>.
    /// </summary>
    public required IDensityFunction FluidLevelSpread { get; set; }

    /// <summary>
    /// Gets or sets the preliminary surface-height function, read from <c>preliminary_surface_level</c>.
    /// </summary>
    public required IDensityFunction PreliminarySurfaceLevel { get; set; }

    /// <summary>
    /// Gets or sets the aquifer lava-selection function, read from <c>lava</c>.
    /// </summary>
    public required IDensityFunction Lava { get; set; }

    /// <summary>
    /// Gets or sets the weirdness climate function, read from <c>ridges</c>.
    /// </summary>
    public required IDensityFunction Ridges { get; set; }

    /// <summary>
    /// Gets or sets the temperature climate function, read from <c>temperature</c>.
    /// </summary>
    public required IDensityFunction Temperature { get; set; }

    /// <summary>
    /// Gets or sets the humidity climate function, read from <c>vegetation</c>.
    /// </summary>
    public required IDensityFunction Vegetation { get; set; }

    /// <summary>
    /// Gets or sets the ore vein gap function, read from <c>vein_gap</c>.
    /// </summary>
    public required IDensityFunction VeinGap { get; set; }

    /// <summary>
    /// Gets or sets the ore vein shape function, read from <c>vein_ridged</c>.
    /// </summary>
    public required IDensityFunction VeinRidged { get; set; }

    /// <summary>
    /// Gets or sets the ore vein type-selection function, read from <c>vein_toggle</c>.
    /// </summary>
    public required IDensityFunction VeinToggle { get; set; }
}
