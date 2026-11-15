namespace Obsidian.API.World.DimensionSettings;

/// <summary>
/// Describes a biome source by its type, preset or explicit biome entries.
/// </summary>
public sealed record class DimensionGeneratorBiomeSource
{
    /// <summary>
    /// Gets or sets the biome source type identifier.
    /// </summary>
    public string Type { get; set; }

    /// <summary>
    /// Gets or sets the optional biome source preset identifier.
    /// </summary>
    public string? Preset { get; set; }

    /// <summary>
    /// Gets or sets the optional single-biome identifier for a fixed source.
    /// </summary>
    public string? Biome { get; set; }

    /// <summary>
    /// Gets or sets the optional explicit biome and climate entries.
    /// </summary>
    public List<DimensionGeneratorBiomeEntry>? Biomes { get; set; }
}
