using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Obsidian.API;

/// <summary>
/// Represents a three-dimensional vector. Uses <see cref="int"/>.
/// </summary>
[DebuggerDisplay("{ToString(),nq}")]
public struct Vector : IEquatable<Vector>, INetworkSerializable<Vector>
{
    /// <summary>
    /// The X component of the <see cref="Vector"/>.
    /// </summary>
    public int X { readonly get; set; }

    /// <summary>
    /// The Y component of the <see cref="Vector"/>.
    /// </summary>
    public int Y { readonly get; set; }

    /// <summary>
    /// The Z component of the <see cref="Vector"/>.
    /// </summary>
    public int Z { readonly get; set; }

    /// <summary>
    /// Creates new instance of <see cref="Vector"/> with <see cref="X"/>, <see cref="Y"/> and <see cref="Z"/> set to <paramref name="value"/>.
    /// </summary>
    /// <param name="value">Value of <see cref="X"/>, <see cref="Y"/> and <see cref="Z"/>.</param>
    public Vector(int value)
    {
        X = Y = Z = value;
    }

    /// <summary>
    /// Creates a new instance of <see cref="Vector"/> with specific values.
    /// </summary>
    /// <param name="x">Value of X coordinate.</param>
    /// <param name="y">Value of Y coordinate.</param>
    /// <param name="z">Value of Z coordinate.</param>
    public Vector(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public readonly SoundPosition SoundPosition => new(X, Y, Z);

    /// <summary>
    /// Calculates magnitude of this <see cref="Vector"/>.
    /// </summary>
    public readonly float Magnitude => MathF.Sqrt(MagnitudeSquared());

    /// <summary>
    /// Calculates magnitude of this <see cref="Vector"/> squared.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly float MagnitudeSquared() => Dot(this, this);

    /// <summary>
    /// Indicates whether this <see cref="Vector"/> is near equal to <paramref name="other"/>.
    /// </summary>
    public readonly bool Equals(Vector other) => X == other.X && Y == other.Y && Z == other.Z;

    /// <summary>
    /// Returns <see cref="Vector"/> clamped to the inclusive range of <paramref name="min"/> and <paramref name="max"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Vector Clamp(Vector min, Vector max)
    {
        return Min(Max(this, min), max);
    }

    /// <summary>
    /// Returns <see cref="Vector"/> clamped to fit inside a single minecraft chunk.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Vector ChunkClamp() => Clamp(Zero, ChunkSize);

    /// <summary>
    /// Creates new <see cref="Vector"/> clamped to fit inside a single minecraft chunk.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector ChunkClamped(int x, int y, int z)
    {
        return new(
            Math.Clamp(x, 0, 15),
            Math.Clamp(y, 0, 255),
            Math.Clamp(z, 0, 15)
        );
    }

    /// <summary>
    /// Calculates the distance between two <see cref="Vector"/>s.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Distance(Vector from, Vector to) => MathF.Sqrt(Square(to.X - from.X) + Square(to.Y - from.Y) + Square(to.Z - from.Z));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Square(int number) => number * number;

    internal static Vector Min(Vector value1, Vector value2)
    {
        return new(
            (value1.X < value2.X) ? value1.X : value2.X,
            (value1.Y < value2.Y) ? value1.Y : value2.Y,
            (value1.Z < value2.Z) ? value1.Z : value2.Z
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector Max(Vector value1, Vector value2)
    {
        return new(
            (value1.X > value2.X) ? value1.X : value2.X,
            (value1.Y > value2.Y) ? value1.Y : value2.Y,
            (value1.Z > value2.Z) ? value1.Z : value2.Z
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector Clamp(Vector value, Vector min, Vector max)
    {
        return Min(Max(value, min), max);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector Cross(Vector value1, Vector value2)
    {
        return new(
            (value1.Y * value2.Z) - (value1.Z * value2.Y),
            (value1.Z * value2.X) - (value1.X * value2.Z),
            (value1.X * value2.Y) - (value1.Y * value2.X)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Dot(Vector value1, Vector value2)
    {
        return (value1.X * value2.X) + (value1.Y * value2.Y) + (value1.Z * value2.Z);
    }

    #region Operators
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Vector a, Vector b) => !a.Equals(b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Vector a, Vector b) => a.Equals(b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector operator +(Vector a, Vector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector operator +(Vector a, (int x, int y, int z) b) => new(a.X + b.x, a.Y + b.y, a.Z + b.z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector operator -(Vector a, Vector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector operator -(Vector a) => new(-a.X, -a.Y, -a.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector operator *(Vector a, Vector b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector operator /(Vector a, Vector b) => new(a.X / b.X, a.Y / b.Y, a.Z / b.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector operator %(Vector a, Vector b) => new(a.X % b.X, a.Y % b.Y, a.Z % b.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector operator +(Vector a, int b) => new(a.X + b, a.Y + b, a.Z + b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector operator -(Vector a, int b) => new(a.X - b, a.Y - b, a.Z - b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector operator *(Vector a, int b) => new(a.X * b, a.Y * b, a.Z * b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector operator /(Vector a, int b) => new(a.X / b, a.Y / b, a.Z / b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector operator %(Vector a, int b) => new(a.X % b, a.Y % b, a.Y % b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector operator +(int a, Vector b) => new(a + b.X, a + b.Y, a + b.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector operator -(int a, Vector b) => new(a - b.X, a - b.Y, a - b.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector operator *(int a, Vector b) => new(a * b.X, a * b.Y, a * b.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector operator /(int a, Vector b) => new(a / b.X, a / b.Y, a / b.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector operator %(int a, Vector b) => new(a % b.X, a % b.Y, a % b.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator VectorF(Vector position)
    {
        return new VectorF(position.X, position.Y, position.Z);
    }
    #endregion

    public void Deconstruct(out int x, out int y, out int z)
    {
        x = X;
        y = Y;
        z = Z;
    }

    /// <inheritdoc/>
    public readonly override bool Equals(object? obj)
    {
        return obj is Vector position && Equals(position);
    }

    /// <inheritdoc/>
    public readonly override int GetHashCode() => HashCode.Combine(X, Y, Z);

    /// <summary>
    /// Returns <see cref="Vector"/> formatted as a <see cref="string"/>.
    /// </summary>
    /// <returns>String representaion of this <see cref="Vector"/>.</returns>
    public readonly override string ToString() => $"{X}:{Y}:{Z}";

    #region Constants
    private static readonly Vector ChunkSize = new(15, 255, 15);

    /// <summary>
    /// A read-only field that represents <see cref="Vector"/> with coordinates <c>(0, 0, 0)</c>.
    /// </summary>
    public static readonly Vector Zero = new(0);

    /// <summary>
    /// A read-only field that represents <see cref="Vector"/> with coordinates <c>(1, 1, 1)</c>.
    /// </summary>
    public static readonly Vector One = new(1);

    /// <summary>
    /// A read-only field that represents <see cref="Vector"/> with coordinates <c>(0, 1, 0)</c>.
    /// </summary>
    public static readonly Vector Up = new(0, 1, 0);

    /// <summary>
    /// A read-only field that represents <see cref="Vector"/> with coordinates <c>(0, -1, 0)</c>.
    /// </summary>
    public static readonly Vector Down = new(0, -1, 0);

    /// <summary>
    /// A read-only field that represents <see cref="Vector"/> with coordinates <c>(-1, 0, 0)</c>.
    /// </summary>
    public static readonly Vector Left = new(-1, 0, 0);

    /// <summary>
    /// A read-only field that represents <see cref="Vector"/> with coordinates <c>(1, 0, 0)</c>.
    /// </summary>
    public static readonly Vector Right = new(1, 0, 0);

    /// <summary>
    /// A read-only field that represents <see cref="Vector"/> with coordinates <c>(0, 0, -1)</c>.
    /// </summary>
    public static readonly Vector Backwards = new(0, 0, -1);

    /// <summary>
    /// A read-only field that represents <see cref="Vector"/> with coordinates <c>(0, 0, 1)</c>.
    /// </summary>
    public static readonly Vector Forwards = new(0, 0, 1);

    /// <summary>
    /// A read-only field that represents <see cref="Vector"/> with coordinates <c>(1, 0, 0)</c>.
    /// </summary>
    public static readonly Vector East = new(1, 0, 0);

    /// <summary>
    /// A read-only field that represents <see cref="Vector"/> with coordinates <c>(-1, 0, 0)</c>.
    /// </summary>
    public static readonly Vector West = new(-1, 0, 0);

    /// <summary>
    /// A read-only field that represents <see cref="Vector"/> with coordinates <c>(0, 0, -1)</c>.
    /// </summary>
    public static readonly Vector North = new(0, 0, -1);

    /// <summary>
    /// A read-only field that represents <see cref="Vector"/> with coordinates <c>(0, 0, 1)</c>.
    /// </summary>
    public static readonly Vector South = new(0, 0, 1);

    /// <summary>
    /// Enumerable array of Cardinal Directions
    /// </summary>
    public static readonly IEnumerable<Vector> CardinalDirs = new[] { North, South, West, East };

    internal static readonly Vector[] AllDirections = [North, South, West, East, Up, Down];

    private string GetDebuggerDisplay()
    {
        return ToString();
    }
    #endregion

    public static void Write(Vector value, INetStreamWriter writer) => writer.WritePosition(value);
    public static Vector Read(INetStreamReader reader) => reader.ReadPosition();
}
