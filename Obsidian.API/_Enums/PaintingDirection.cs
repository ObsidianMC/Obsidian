namespace Obsidian.API;

/// <summary>The direction a painting faces, numbered as horizontal directions are in the protocol.</summary>
public enum PaintingDirection : byte
{
    /// <summary>Facing south.</summary>
    South,
    /// <summary>Facing west.</summary>
    West,
    /// <summary>Facing north.</summary>
    North,
    /// <summary>Facing east.</summary>
    East
}
