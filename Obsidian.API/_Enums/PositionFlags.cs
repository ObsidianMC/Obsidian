namespace Obsidian.API;

/// <summary>Which fields of a teleport are relative to the entity's current values instead of absolute.</summary>
[Flags]
public enum PositionFlags
{
    /// <summary>Every field is absolute.</summary>
    None = 0,
    // Vanilla 26.3 Relative packs these nine relative-movement fields into an int mask.
    /// <summary>The X coordinate is added to the current one.</summary>
    X = 1 << 0,
    /// <summary>The Y coordinate is added to the current one.</summary>
    Y = 1 << 1,
    /// <summary>The Z coordinate is added to the current one.</summary>
    Z = 1 << 2,
    /// <summary>The yaw is added to the current one.</summary>
    RotationY = 1 << 3,
    /// <summary>The pitch is added to the current one.</summary>
    RotationX = 1 << 4,
    /// <summary>The X velocity is added to the current one.</summary>
    DeltaX = 1 << 5,
    /// <summary>The Y velocity is added to the current one.</summary>
    DeltaY = 1 << 6,
    /// <summary>The Z velocity is added to the current one.</summary>
    DeltaZ = 1 << 7,
    /// <summary>The velocity is turned by the same angle as the rotation changes.</summary>
    RotateDelta = 1 << 8
}

