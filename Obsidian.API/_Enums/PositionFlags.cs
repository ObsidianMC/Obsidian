namespace Obsidian.API;

[Flags]
public enum PositionFlags
{
    None = 0,
    // Vanilla 26.3 Relative packs these nine relative-movement fields into an int mask.
    X = 1 << 0,
    Y = 1 << 1,
    Z = 1 << 2,
    RotationY = 1 << 3,
    RotationX = 1 << 4,
    DeltaX = 1 << 5,
    DeltaY = 1 << 6,
    DeltaZ = 1 << 7,
    RotateDelta = 1 << 8
}

