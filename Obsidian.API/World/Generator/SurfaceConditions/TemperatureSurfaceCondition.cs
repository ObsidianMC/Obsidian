namespace Obsidian.API.World.Generator.SurfaceConditions;

/// <summary>
/// Tests the biome temperature condition used for frozen surfaces.
/// </summary>
[SurfaceCondition("minecraft:temperature")]
public sealed class TemperatureSurfaceCondition : ISurfaceCondition
{
    /// <summary>
    /// Gets the surface condition codec type identifier.
    /// </summary>
    public string Type => "minecraft:temperature";
}
