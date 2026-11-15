namespace Obsidian.API.World.DimensionSettings;

/// <summary>
/// Describes the generator and biome source of a dimension.
/// </summary>
public sealed record class DimensionGeneratorSettings
{
    /// <summary>
    /// Gets or sets the dimension generator's settings object.
    /// </summary>
    public IDimensionSetting Settings { get; set; }
    /// <summary>
    /// Gets or sets the dimension generator type identifier.
    /// </summary>
    public string Type { get; set; }

    /// <summary>
    /// Gets or sets the optional dimension identifier attached to these settings.
    /// </summary>
    public string? Dimension { get; set; }

    /// <summary>
    /// Gets or sets the biome source definition for this generator.
    /// </summary>
    public DimensionGeneratorBiomeSource BiomeSource { get; set; }
}
