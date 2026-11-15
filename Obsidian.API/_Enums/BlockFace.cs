namespace Obsidian.API;
/// <summary>One of the six faces of a block, in the order the protocol numbers them.</summary>
public enum BlockFace
{
    /// <summary>The bottom face, facing -Y.</summary>
    Down,
    /// <summary>The top face, facing +Y.</summary>
    Up,
    /// <summary>The north face, facing -Z.</summary>
    North,
    /// <summary>The south face, facing +Z.</summary>
    South,
    /// <summary>The west face, facing -X.</summary>
    West,
    /// <summary>The east face, facing +X.</summary>
    East,
}
