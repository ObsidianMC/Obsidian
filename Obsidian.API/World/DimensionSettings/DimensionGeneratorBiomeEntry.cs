namespace Obsidian.API.World.DimensionSettings;

/// <summary>
/// Associates a biome identifier with climate selection parameters.
/// </summary>
public sealed record class DimensionGeneratorBiomeEntry
{
    /// <summary>
    /// Gets or sets the biome identifier selected by this entry.
    /// </summary>
    public string Biome { get; set; }

    /// <summary>
    /// Gets or sets the climate values or intervals used to select this biome.
    /// </summary>
    public DimensionGeneratorBiomeParameters Parameters { get; set; }
}
