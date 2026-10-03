namespace Obsidian.WorldData.Features;

/// <summary>
/// Which cave surface a feature grows on, like vanilla's CaveSurface (<c>floor</c> or <c>ceiling</c>).
/// </summary>
public enum CaveSurface
{
    /// <summary>
    /// Grows on ceilings; the surface lies upward.
    /// </summary>
    Ceiling,

    /// <summary>
    /// Grows on floors; the surface lies downward.
    /// </summary>
    Floor
}

internal static class CaveSurfaceExtensions
{
    /// <summary>
    /// Direction from the open space toward the surface (down for floors, up for ceilings).
    /// </summary>
    public static BlockFace Direction(this CaveSurface surface) => surface == CaveSurface.Floor ? BlockFace.Down : BlockFace.Up;
}
