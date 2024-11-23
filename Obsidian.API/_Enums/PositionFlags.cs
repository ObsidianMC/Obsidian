namespace Obsidian.API;

[Flags]
public enum PositionFlags : sbyte
{
    None,
    X,
    Y,
    Z,
    RotationY,
    RotationX,
    DeltaX,
    DeltaY,
    DeltaZ,
    RotateDelta
}

