namespace Obsidian.API.World.Generator.Noise;
/// <summary>
/// Contains the block defaults, noise router and surface rules for a noise-based generator.
/// </summary>
public sealed class NoiseSetting
{
    /// <summary>
    /// Gets or sets whether aquifer generation is enabled by <c>aquifers_enabled</c>.
    /// </summary>
    public bool AquifersEnabled { get; set; }

    /// <summary>
    /// Gets or sets the terrain's default solid state, read from <c>default_block</c>.
    /// </summary>
    public SimpleBlockState DefaultBlock { get; set; }

    /// <summary>
    /// Gets or sets the terrain's default fluid state, read from <c>default_fluid</c>.
    /// </summary>
    public SimpleBlockState DefaultFluid { get; set; }

    /// <summary>
    /// Gets or sets the <c>disable_mob_generation</c> setting for generation-time spawning.
    /// </summary>
    public bool DisableMobGeneration { get; set; }

    /// <summary>
    /// Gets or sets whether <c>legacy_random_source</c> selects the legacy random source.
    /// </summary>
    public bool LegacyRandomSource { get; set; }

    /// <summary>
    /// Gets or sets the generation bounds and noise sampling cell dimensions.
    /// </summary>
    public NoiseConfiguration Noise { get; set; }

    /// <summary>
    /// Gets or sets the density functions read from <c>noise_router</c>.
    /// </summary>
    public NoiseRouter NoiseRouter { get; set; }

    /// <summary>
    /// Gets or sets whether ore vein generation is enabled by <c>ore_veins_enabled</c>.
    /// </summary>
    public bool OreVeinsEnabled { get; set; }

    /// <summary>
    /// Gets or sets the sea surface's world Y in blocks, read from <c>sea_level</c>.
    /// </summary>
    public int SeaLevel { get; set; }

    /// <summary>
    /// Gets or sets the preferred spawn climate intervals read from <c>spawn_target</c>.
    /// </summary>
    public ImmutableArray<ClimateParameter> SpawnTarget { get; set; } = [];

    /// <summary>
    /// Gets or sets the surface block rule read from <c>surface_rule</c>.
    /// </summary>
    public ISurfaceRule SurfaceRule { get; set; }
}
