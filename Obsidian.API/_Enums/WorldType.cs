namespace Obsidian.API;

/// <summary>
/// The kinds of vanilla world. Obsidian can generate only <see cref="Default"/> and <see cref="Flat"/>; creating
/// another kind throws <see cref="NotSupportedException"/>.
/// </summary>
public enum WorldType
{
    /// <summary>The normal world.</summary>
    Default,
    /// <summary>A flat world.</summary>
    Flat,
    /// <summary>A world with biomes 16 times as large.</summary>
    LargeBiomes,
    /// <summary>A world with much taller terrain.</summary>
    Amplified
}
