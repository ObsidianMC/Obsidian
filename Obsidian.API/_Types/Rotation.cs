namespace Obsidian.API;

/// <summary>A rotation given as angles around the X, Y and Z axes.</summary>
public record struct Rotation
{
    /// <summary>The rotation around the X axis.</summary>
    public double X;
    /// <summary>The rotation around the Y axis.</summary>
    public double Y;
    /// <summary>The rotation around the Z axis.</summary>
    public double Z;
}
