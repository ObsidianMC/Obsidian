namespace Obsidian.API.World.Generator.SurfaceConditions;

/// <summary>
/// Tests whether neighboring surface heights form a steep slope.
/// </summary>
[SurfaceCondition("minecraft:steep")]
public sealed class SteepSurfaceCondition : ISurfaceCondition
{
    /// <summary>
    /// Gets the surface condition codec type identifier.
    /// </summary>
    public string Type => "minecraft:steep";
}
