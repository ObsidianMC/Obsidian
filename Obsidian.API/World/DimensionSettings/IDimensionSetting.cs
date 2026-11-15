namespace Obsidian.API.World.DimensionSettings;

/// <summary>
/// Exposes the type identifier of dimension settings.
/// </summary>
public interface IDimensionSetting
{
    /// <summary>
    /// Gets or sets the settings type identifier.
    /// </summary>
    public string Type { get; set; }
}
