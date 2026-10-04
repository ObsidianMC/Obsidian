using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Obsidian.API;

/// <summary>
/// Represents a three-dimensional vector. Uses <see cref="double"/>, like vanilla's positions and movement
/// (<c>Vec3</c>), which keep their precision far from the world's origin where a <see cref="VectorF"/> doesn't.
/// </summary>
/// <remarks>
/// On the network it's three doubles (vanilla's <c>Vec3.STREAM_CODEC</c>).
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
public struct VectorD : IEquatable<VectorD>, INetworkSerializable<VectorD>
{
    /// <summary>
    /// The X component of the <see cref="VectorD"/>.
    /// </summary>
    public double X { readonly get; set; }

    /// <summary>
    /// The Y component of the <see cref="VectorD"/>.
    /// </summary>
    public double Y { readonly get; set; }

    /// <summary>
    /// The Z component of the <see cref="VectorD"/>.
    /// </summary>
    public double Z { readonly get; set; }

    /// <summary>
    /// Creates new instance of <see cref="VectorD"/> with <see cref="X"/>, <see cref="Y"/> and <see cref="Z"/> set to <paramref name="value"/>.
    /// </summary>
    public VectorD(double value)
    {
        X = Y = Z = value;
    }

    /// <summary>
    /// Creates a new instance of <see cref="VectorD"/> with specific values.
    /// </summary>
    public VectorD(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>
    /// Calculates magnitude of this <see cref="VectorD"/>.
    /// </summary>
    public readonly double Magnitude => Math.Sqrt(MagnitudeSquared());

    /// <summary>
    /// Calculates magnitude of this <see cref="VectorD"/> squared.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly double MagnitudeSquared() => Dot(this, this);

    /// <summary>
    /// Indicates whether this <see cref="VectorD"/> has exactly the same parts as <paramref name="other"/>; see
    /// <see cref="IsNear"/> to compare with a tolerance.
    /// </summary>
    public readonly bool Equals(VectorD other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    /// <summary>
    /// Indicates whether each part of this <see cref="VectorD"/> is within <paramref name="tolerance"/> of
    /// <paramref name="other"/>'s.
    /// </summary>
    public readonly bool IsNear(VectorD other, double tolerance = 0.01) =>
        Math.Abs(X - other.X) <= tolerance && Math.Abs(Y - other.Y) <= tolerance && Math.Abs(Z - other.Z) <= tolerance;

    /// <summary>
    /// Rounds each part of this <see cref="VectorD"/> down to a whole number.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly VectorD Floor() => new(Math.Floor(X), Math.Floor(Y), Math.Floor(Z));

    /// <summary>
    /// Performs vector normalization on this <see cref="VectorD"/>'s coordinates.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly VectorD Normalize()
    {
        double magnitude = Magnitude;
        return new(X / magnitude, Y / magnitude, Z / magnitude);
    }

    /// <summary>
    /// Returns <see cref="VectorD"/> clamped to the inclusive range of <paramref name="min"/> and <paramref name="max"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly VectorD Clamp(VectorD min, VectorD max) => Clamp(this, min, max);

    /// <summary>
    /// Calculates the distance between two <see cref="VectorD"/>s.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Distance(VectorD from, VectorD to) => (to - from).Magnitude;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorD Clamp(VectorD value, VectorD min, VectorD max) =>
        new(Math.Clamp(value.X, min.X, max.X), Math.Clamp(value.Y, min.Y, max.Y), Math.Clamp(value.Z, min.Z, max.Z));

    /// <summary>The smaller of each part of two vectors.</summary>
    public static VectorD Min(VectorD value1, VectorD value2) =>
        new(Math.Min(value1.X, value2.X), Math.Min(value1.Y, value2.Y), Math.Min(value1.Z, value2.Z));

    /// <summary>The larger of each part of two vectors.</summary>
    public static VectorD Max(VectorD value1, VectorD value2) =>
        new(Math.Max(value1.X, value2.X), Math.Max(value1.Y, value2.Y), Math.Max(value1.Z, value2.Z));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorD Cross(VectorD value1, VectorD value2)
    {
        return new(
            (value1.Y * value2.Z) - (value1.Z * value2.Y),
            (value1.Z * value2.X) - (value1.X * value2.Z),
            (value1.X * value2.Y) - (value1.Y * value2.X)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Dot(VectorD value1, VectorD value2) =>
        (value1.X * value2.X) + (value1.Y * value2.Y) + (value1.Z * value2.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorD Lerp(VectorD value1, VectorD value2, double amount) => value1 + (value2 - value1) * amount;

    #region Operators
    public static bool operator !=(VectorD a, VectorD b) => !a.Equals(b);

    public static bool operator ==(VectorD a, VectorD b) => a.Equals(b);

    public static VectorD operator +(VectorD a, VectorD b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static VectorD operator +(VectorD a, (int x, int y, int z) b) => new(a.X + b.x, a.Y + b.y, a.Z + b.z);

    public static VectorD operator -(VectorD a, VectorD b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static VectorD operator -(VectorD a) => new(-a.X, -a.Y, -a.Z);

    public static VectorD operator *(VectorD a, VectorD b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);

    public static VectorD operator /(VectorD a, VectorD b) => new(a.X / b.X, a.Y / b.Y, a.Z / b.Z);

    public static VectorD operator +(VectorD a, double b) => new(a.X + b, a.Y + b, a.Z + b);

    public static VectorD operator -(VectorD a, double b) => new(a.X - b, a.Y - b, a.Z - b);

    public static VectorD operator *(VectorD a, double b) => new(a.X * b, a.Y * b, a.Z * b);

    public static VectorD operator /(VectorD a, double b) => new(a.X / b, a.Y / b, a.Z / b);

    public static VectorD operator *(double a, VectorD b) => new(a * b.X, a * b.Y, a * b.Z);

    /// <summary>A block position as the position of its lowest corner.</summary>
    public static implicit operator VectorD(Vector value) => new(value.X, value.Y, value.Z);

    /// <summary>A <see cref="VectorF"/> fits in a <see cref="VectorD"/> exactly.</summary>
    public static implicit operator VectorD(VectorF value) => new(value.X, value.Y, value.Z);

    /// <summary>Loses precision: doubles round to the nearest float.</summary>
    public static explicit operator VectorF(VectorD value) => new((float)value.X, (float)value.Y, (float)value.Z);

    /// <summary>The block a position is in: each part rounded down, as vanilla's <c>BlockPos.containing</c> does.</summary>
    public static explicit operator Vector(VectorD value) =>
        new((int)Math.Floor(value.X), (int)Math.Floor(value.Y), (int)Math.Floor(value.Z));
    #endregion

    public readonly void Deconstruct(out double x, out double y, out double z)
    {
        x = X;
        y = Y;
        z = Z;
    }

    /// <summary>Writes the vector as three doubles.</summary>
    public static void Write(VectorD value, INetStreamWriter writer)
    {
        writer.WriteDouble(value.X);
        writer.WriteDouble(value.Y);
        writer.WriteDouble(value.Z);
    }

    /// <summary>Reads a vector of three doubles.</summary>
    public static VectorD Read(INetStreamReader reader) => new(reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble());

    /// <inheritdoc/>
    public readonly override bool Equals(object? obj) => obj is VectorD position && Equals(position);

    /// <inheritdoc/>
    public readonly override int GetHashCode() => HashCode.Combine(X, Y, Z);

    /// <summary>
    /// Returns <see cref="VectorD"/> formatted as a <see cref="string"/>.
    /// </summary>
    public readonly override string ToString() => $"{X:0.0}:{Y:0.0}:{Z:0.0}";

    #region Constants
    /// <summary>
    /// A read-only field that represents <see cref="VectorD"/> with coordinates <c>(0, 0, 0)</c>.
    /// </summary>
    public static readonly VectorD Zero = new(0d);

    /// <summary>
    /// A read-only field that represents <see cref="VectorD"/> with coordinates <c>(1, 1, 1)</c>.
    /// </summary>
    public static readonly VectorD One = new(1d);

    /// <summary>
    /// A read-only field that represents <see cref="VectorD"/> with coordinates <c>(0, 1, 0)</c>.
    /// </summary>
    public static readonly VectorD Up = new(0d, 1d, 0d);

    /// <summary>
    /// A read-only field that represents <see cref="VectorD"/> with coordinates <c>(0, -1, 0)</c>.
    /// </summary>
    public static readonly VectorD Down = new(0d, -1d, 0d);
    #endregion
}
