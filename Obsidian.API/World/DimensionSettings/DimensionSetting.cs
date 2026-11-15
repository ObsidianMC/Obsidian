namespace Obsidian.API.World.DimensionSettings;

/// <summary>
/// Associates a dimension type with its generator definition.
/// </summary>
public sealed record class DimensionSetting
{
    /// <summary>
    /// Gets or sets the dimension type identifier.
    /// </summary>
    public string Type { get; set; }

    /// <summary>
    /// Gets or sets the generator definition for the dimension.
    /// </summary>
    public DimensionGeneratorSettings Generator { get; set; }
}
