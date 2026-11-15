using Obsidian.API.World.DimensionSettings;

namespace Obsidian.API.World;

/// <summary>
/// Holds a world's seed, feature options and dimension generator definitions.
/// </summary>
public sealed class WorldGenSetting
{
    /// <summary>
    /// Gets or sets whether the world requests a bonus chest.
    /// </summary>
    public bool BonusChest { get; set; }
    /// <summary>
    /// Gets or sets the saved feature-generation option.
    /// </summary>
    public bool GenerateFeatures { get; set; }

    /// <summary>
    /// Gets or sets the numeric world generation seed.
    /// </summary>
    public long Seed { get; set; }

    /// <summary>
    /// Gets or sets the dimension definitions indexed by identifier.
    /// </summary>
    public Dictionary<string, DimensionSetting> Dimensions { get; set; }
}
