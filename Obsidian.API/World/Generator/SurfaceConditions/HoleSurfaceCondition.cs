namespace Obsidian.API.World.Generator.SurfaceConditions;

/// <summary>
/// Tests the surface-depth criterion used to identify holes.
/// </summary>
[SurfaceCondition("minecraft:hole")]
public sealed class HoleSurfaceCondition : ISurfaceCondition
{
    /// <summary>
    /// Gets the surface condition codec type identifier.
    /// </summary>
    public string Type => "minecraft:hole";
}
