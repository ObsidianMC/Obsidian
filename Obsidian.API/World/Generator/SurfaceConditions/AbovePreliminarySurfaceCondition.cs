namespace Obsidian.API.World.Generator.SurfaceConditions;

/// <summary>
/// Tests whether a block is above the preliminary terrain surface.
/// </summary>
[SurfaceCondition("minecraft:above_preliminary_surface")]
public sealed class AbovePreliminarySurfaceCondition : ISurfaceCondition
{
    /// <summary>
    /// Gets the surface condition codec type identifier.
    /// </summary>
    public string Type => "minecraft:above_preliminary_surface";
}
