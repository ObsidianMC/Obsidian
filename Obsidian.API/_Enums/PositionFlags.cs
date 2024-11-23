namespace Obsidian.API;

[Flags]
public enum PositionFlags : sbyte
{
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

