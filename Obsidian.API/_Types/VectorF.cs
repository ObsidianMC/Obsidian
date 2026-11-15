using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Obsidian.API;

/// <summary>
/// Represents a three-dimensional vector. Uses <see cref="float"/>.
/// </summary>
[DebuggerDisplay("{ToString(),nq}")]
public struct VectorF : IEquatable<VectorF>
{
    /// <summary>
    /// The X component of the <see cref="VectorF"/>.
    /// </summary>
    public float X
    {
        readonly get;
        set;
    }

    /// <summary>
    /// The Y component of the <see cref="VectorF"/>.
    /// </summary>
    public float Y
    {
        readonly get;
        set;
    }

    /// <summary>
    /// The Z component of the <see cref="VectorF"/>.
    /// </summary>
    public float Z
    {
        readonly get;
        set;
    }

    /// <summary>
    /// Creates new instance of <see cref="VectorF"/> with <see cref="X"/>, <see cref="Y"/> and <see cref="Z"/> set to <paramref name="value"/>.
    /// </summary>
    /// <param name="value">Value of <see cref="X"/>, <see cref="Y"/> and <see cref="Z"/>.</param>
    public VectorF(float value)
    {
        X = Y = Z = value;
    }

    /// <summary>
    /// Creates a new instance of <see cref="VectorF"/> with specific values.
    /// </summary>
    /// <param name="x">Value of X coordinate.</param>
    /// <param name="y">Value of Y coordinate.</param>
    /// <param name="z">Value of Z coordinate.</param>
    public VectorF(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>
    /// Creates a new instance of <see cref="VectorF"/> with specific values.
    /// </summary>
    /// <param name="x">Value of X coordinate.</param>
    /// <param name="y">Value of Y coordinate.</param>
    /// <param name="z">Value of Z coordinate.</param>
    public VectorF(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>
    /// Calculates magnitude of this <see cref="VectorF"/>.
    /// </summary>
    public readonly float Magnitude => MathF.Sqrt(MagnitudeSquared());

    /// <summary>
    /// Calculates magnitude of this <see cref="VectorF"/> squared.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly float MagnitudeSquared() => Dot(this, this);

    /// <summary>
    /// Indicates whether this <see cref="VectorF"/> is near equal to <paramref name="other"/>.
    /// </summary>
    public readonly bool Equals(VectorF other) => IsNear(X, other.X) && IsNear(Y, other.Y) && IsNear(Z, other.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNear(float a, float b) => MathF.Abs(a - b) <= 0.01f;

    /// <summary>
    /// Truncates the decimal component of each part of this <see cref="VectorF"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly VectorF Floor() => new(MathF.Floor(X), MathF.Floor(Y), MathF.Floor(Z));

    /// <summary>
    /// Performs vector normalization on this <see cref="VectorF"/>'s coordinates.
    /// </summary>
    /// <returns>Normalized <see cref="VectorF"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly VectorF Normalize()
    {
        float magnitude = Magnitude;
        return new(X / magnitude, Y / magnitude, Z / magnitude);
    }

    /// <summary>
    /// Returns <see cref="VectorF"/> clamped to the inclusive range of <paramref name="min"/> and <paramref name="max"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly VectorF Clamp(VectorF min, VectorF max)
    {
        return Min(Max(this, min), max);
    }

    /// <summary>
    /// Creates new <see cref="Vector"/> clamped to fit inside a single minecraft chunk.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorF ChunkClamped(float x, float y, float z)
    {
        return new(
            Math.Clamp(x, 0f, 15f),
            Math.Clamp(y, 0f, 255f),
            Math.Clamp(z, 0f, 15f)
        );
    }

    /// <summary>
    /// Calculates the distance between two <see cref="VectorF"/>s.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Distance(VectorF from, VectorF to) => MathF.Sqrt(Square(to.X - from.X) + Square(to.Y - from.Y) + Square(to.Z - from.Z));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Square(float number) => number * number;

    internal static VectorF Min(VectorF value1, VectorF value2)
    {
        return new(
            (value1.X < value2.X) ? value1.X : value2.X,
            (value1.Y < value2.Y) ? value1.Y : value2.Y,
            (value1.Z < value2.Z) ? value1.Z : value2.Z
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static VectorF Max(VectorF value1, VectorF value2)
    {
        return new(
            (value1.X > value2.X) ? value1.X : value2.X,
            (value1.Y > value2.Y) ? value1.Y : value2.Y,
            (value1.Z > value2.Z) ? value1.Z : value2.Z
        );
    }

    /// <summary>
    /// Clamps each component of <paramref name="value"/> between the matching components of <paramref name="min"/> and
    /// <paramref name="max"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorF Clamp(VectorF value, VectorF min, VectorF max)
    {
        return Min(Max(value, min), max);
    }

    /// <summary>Gets the cross product of two vectors.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorF Cross(VectorF value1, VectorF value2)
    {
        return new(
            (value1.Y * value2.Z) - (value1.Z * value2.Y),
            (value1.Z * value2.X) - (value1.X * value2.Z),
            (value1.X * value2.Y) - (value1.Y * value2.X)
        );
    }

    /// <summary>Gets the dot product of two vectors.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Dot(VectorF value1, VectorF value2)
    {
        return (value1.X * value2.X) + (value1.Y * value2.Y) + (value1.Z * value2.Z);
    }

    /// <summary>Interpolates linearly between two vectors.</summary>
    /// <param name="value1">The vector returned when <paramref name="amount"/> is 0.</param>
    /// <param name="value2">The vector returned when <paramref name="amount"/> is 1.</param>
    /// <param name="amount">How far to move from <paramref name="value1"/> toward <paramref name="value2"/>; values
    /// outside 0 to 1 extrapolate.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorF Lerp(VectorF value1, VectorF value2, float amount)
    {
        return new(
            value1.X * (1f - amount) + (value2.X * amount),
            value1.Y * (1f - amount) + (value2.Y * amount),
            value1.Z * (1f - amount) + (value2.Z * amount)
        );
    }

    #region Operators
    /// <summary>Indicates whether any component of the two vectors differs by more than 0.01.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(VectorF a, VectorF b) => !a.Equals(b);

    /// <summary>Indicates whether every component of the two vectors is within 0.01 of the other's.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(VectorF a, VectorF b) => a.Equals(b);

    /// <summary>Adds two vectors component by component.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorF operator +(VectorF a, VectorF b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    /// <summary>Adds the tuple's values to the matching components.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorF operator +(VectorF a, (int x, int y, int z) b) => new(a.X + b.x, a.Y + b.y, a.Z + b.z);

    /// <summary>Subtracts <paramref name="b"/> from <paramref name="a"/> component by component.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorF operator -(VectorF a, VectorF b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    /// <summary>Negates every component.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorF operator -(VectorF a) => new(-a.X, -a.Y, -a.Z);

    /// <summary>Multiplies two vectors component by component.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorF operator *(VectorF a, VectorF b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);

    /// <summary>Divides <paramref name="a"/> by <paramref name="b"/> component by component.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorF operator /(VectorF a, VectorF b) => new(a.X / b.X, a.Y / b.Y, a.Z / b.Z);

    /// <summary>
    /// Takes the remainder of dividing <paramref name="a"/> by <paramref name="b"/>, component by component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorF operator %(VectorF a, VectorF b) => new(a.X % b.X, a.Y % b.Y, a.Z % b.Z);

    /// <summary>Adds <paramref name="b"/> to every component.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorF operator +(VectorF a, float b) => new(a.X + b, a.Y + b, a.Z + b);

    /// <summary>Subtracts <paramref name="b"/> from every component.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorF operator -(VectorF a, float b) => new(a.X - b, a.Y - b, a.Z - b);

    /// <summary>Multiplies every component by <paramref name="b"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorF operator *(VectorF a, float b) => new(a.X * b, a.Y * b, a.Z * b);

    /// <summary>Divides every component by <paramref name="b"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorF operator /(VectorF a, float b) => new(a.X / b, a.Y / b, a.Z / b);

    /// <summary>Takes the remainder of dividing every component by <paramref name="b"/>.</summary>
    /// <remarks>The Z component is currently computed from Y instead of Z.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorF operator %(VectorF a, float b) => new(a.X % b, a.Y % b, a.Y % b);

    /// <summary>Adds <paramref name="a"/> to every component of <paramref name="b"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorF operator +(float a, VectorF b) => new(a + b.X, a + b.Y, a + b.Z);

    /// <summary>Subtracts every component of <paramref name="b"/> from <paramref name="a"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorF operator -(float a, VectorF b) => new(a - b.X, a - b.Y, a - b.Z);

    /// <summary>Multiplies every component of <paramref name="b"/> by <paramref name="a"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorF operator *(float a, VectorF b) => new(a * b.X, a * b.Y, a * b.Z);

    /// <summary>Divides <paramref name="a"/> by every component of <paramref name="b"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorF operator /(float a, VectorF b) => new(a / b.X, a / b.Y, a / b.Z);

    /// <summary>
    /// Takes the remainder of dividing <paramref name="a"/> by every component of <paramref name="b"/>.
    /// </summary>
    /// <remarks>The Z component is currently computed from Y instead of Z.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorF operator %(float a, VectorF b) => new(a % b.X, a % b.Y, a % b.Y);

    /// <summary>
    /// Converts to a <see cref="Vector"/> by casting each component to <see cref="int"/>, which truncates toward zero.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Vector(VectorF positionF)
    {
        return new Vector((int)positionF.X, (int)positionF.Y, (int)positionF.Z);
    }
    #endregion

    /// <summary>Splits the vector into its X, Y and Z components.</summary>
    public void Deconstruct(out float x, out float y, out float z)
    {
        x = X;
        y = Y;
        z = Z;
    }

    /// <inheritdoc/>
    public readonly override bool Equals(object? obj)
    {
        return obj is VectorF position && Equals(position);
    }

    /// <inheritdoc/>
    public readonly override int GetHashCode() => HashCode.Combine(X, Y, Z);

    /// <summary>
    /// Returns <see cref="VectorF"/> formatted as a <see cref="string"/>.
    /// </summary>
    /// <returns>String representaion of this <see cref="VectorF"/>.</returns>
    public readonly override string ToString() => $"{X:0.0}:{Y:0.0}:{Z:0.0}";

    #region Constants
    /// <summary>
    /// A read-only field that represents <see cref="VectorF"/> with coordinates <c>(0, 0, 0)</c>.
    /// </summary>
    public static readonly VectorF Zero = new(0f);

    /// <summary>
    /// A read-only field that represents <see cref="VectorF"/> with coordinates <c>(1, 1, 1)</c>.
    /// </summary>
    public static readonly VectorF One = new(1f);

    /// <summary>
    /// A read-only field that represents <see cref="VectorF"/> with coordinates <c>(0, 1, 0)</c>.
    /// </summary>
    public static readonly VectorF Up = new(0f, 1f, 0f);

    /// <summary>
    /// A read-only field that represents <see cref="VectorF"/> with coordinates <c>(0, -1, 0)</c>.
    /// </summary>
    public static readonly VectorF Down = new(0f, -1f, 0f);

    /// <summary>
    /// A read-only field that represents <see cref="VectorF"/> with coordinates <c>(-1, 0, 0)</c>.
    /// </summary>
    public static readonly VectorF Left = new(-1f, 0f, 0f);

    /// <summary>
    /// A read-only field that represents <see cref="VectorF"/> with coordinates <c>(1, 0, 0)</c>.
    /// </summary>
    public static readonly VectorF Right = new(1f, 0f, 0f);

    /// <summary>
    /// A read-only field that represents <see cref="VectorF"/> with coordinates <c>(0, 0, -1)</c>.
    /// </summary>
    public static readonly VectorF Backwards = new(0f, 0f, -1f);

    /// <summary>
    /// A read-only field that represents <see cref="VectorF"/> with coordinates <c>(0, 0, 1)</c>.
    /// </summary>
    public static readonly VectorF Forwards = new(0f, 0f, 1f);

    /// <summary>
    /// A read-only field that represents <see cref="VectorF"/> with coordinates <c>(1, 0, 0)</c>.
    /// </summary>
    public static readonly VectorF East = new(1f, 0f, 0f);

    /// <summary>
    /// A read-only field that represents <see cref="VectorF"/> with coordinates <c>(-1, 0, 0)</c>.
    /// </summary>
    public static readonly VectorF West = new(-1f, 0f, 0f);

    /// <summary>
    /// A read-only field that represents <see cref="VectorF"/> with coordinates <c>(0, 0, -1)</c>.
    /// </summary>
    public static readonly VectorF North = new(0f, 0f, -1f);

    /// <summary>
    /// A read-only field that represents <see cref="VectorF"/> with coordinates <c>(0, 0, 1)</c>.
    /// </summary>
    public static readonly VectorF South = new(0f, 0f, 1f);
    #endregion
}
