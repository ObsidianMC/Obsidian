namespace Obsidian.API;

/// <summary>
/// Represents velocity of an entity in the world.
/// </summary>
/// <remarks>
/// Creates a new instance of <see cref="Velocity"/> with specific values.
/// </remarks>
/// <param name="x">Velocity on the X axis.</param>
/// <param name="y">Velocity on the Y axis.</param>
/// <param name="z">Velocity on the Z axis.</param>
public struct Velocity(double x, double y, double z)
{
    /// <summary>
    /// Velocity on the X axis.
    /// </summary>
    public double X { get; set; } = x;
    /// <summary>
    /// Velocity on the Y axis.
    /// </summary>
    public double Y { get; set; } = y;
    /// <summary>
    /// Velocity on the Z axis.
    /// </summary>
    public double Z { get; set; } = z;

    public static readonly Velocity Zero = new(0, 0, 0);

    /// <summary>
    /// Returns <see cref="Velocity"/> expressed as how many blocks on each axis can be travelled in a second.
    /// </summary>
    /// <param name="x">How many blocks can be travelled on the X axis in a second.</param>
    /// <param name="y">How many blocks can be travelled on the Y axis in a second.</param>
    /// <param name="z">How many blocks can be travelled on the Z axis in a second.</param>
    public static Velocity FromBlockPerSecond(float x, float y, float z)
    {
        return new Velocity(x / 20f, y / 20f, z / 20f);
    }

    /// <summary>
    /// Returns <see cref="Velocity"/> expressed as how many blocks on each axis can be travelled in a tick (50ms).
    /// </summary>
    /// <param name="x">How many blocks can be travelled on the X axis in a tick (50ms).</param>
    /// <param name="y">How many blocks can be travelled on the Y axis in a tick (50ms).</param>
    /// <param name="z">How many blocks can be travelled on the Z axis in a tick (50ms).</param>
    public static Velocity FromBlockPerTick(float x, float y, float z)
    {
        return new Velocity(x, y, z);
    }

    /// <summary>
    /// Turns <see cref="Vector"/> into <see cref="Velocity"/>, using it's coordinates as to how many blocks can be travelled per second.
    /// </summary>
    /// <param name="vector"><see cref="Vector"/> to be used for conversion.</param>
    public static Velocity FromVector(Vector vector)
    {
        return FromBlockPerSecond(vector.X, vector.Y, vector.Z);
    }

    /// <summary>
    /// Turns <see cref="VectorF"/> into <see cref="Velocity"/>, using it's coordinates as to how many blocks can be travelled per second.
    /// </summary>
    /// <param name="vector"><see cref="VectorF"/> to be used for conversion.</param>
    public static Velocity FromVector(VectorF vector)
    {
        return FromBlockPerSecond(vector.X, vector.Y, vector.Z);
    }

    /// <summary>
    /// Returns such velocity, that can travel from <paramref name="from"/> to <paramref name="to"/> in a second.
    /// </summary>
    /// <param name="from">Starting position.</param>
    /// <param name="to">Target position.</param>
    public static Velocity FromDirection(Vector from, Vector to)
    {
        return FromVector(to - from);
    }

    /// <summary>
    /// Returns such velocity, that can travel from <paramref name="from"/> to <paramref name="to"/> in a second.
    /// </summary>
    /// <param name="from">Starting position.</param>
    /// <param name="to">Target position.</param>
    public static Velocity FromDirection(VectorF from, VectorF to)
    {
        return FromVector(to - from);
    }
}
