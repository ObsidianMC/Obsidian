using System;
using System.Runtime.CompilerServices;

namespace Obsidian.API
{
    /// <summary>
    /// Represents position in three-dimensional space. Uses <see cref="float"/>.
    /// </summary>
    public struct PositionF : IEquatable<PositionF>
    {
        /// <summary>
        /// X component of the <see cref="PositionF"/>.
        /// </summary>
        public float X { get; set; }
        /// <summary>
        /// Y component of the <see cref="PositionF"/>.
        /// </summary>
        public float Y { get; set; }
        /// <summary>
        /// Z component of the <see cref="PositionF"/>.
        /// </summary>
        public float Z { get; set; }

        /// <summary>
        /// Creates new instance of <see cref="PositionF"/> with <see cref="X"/>, <see cref="Y"/> and <see cref="Z"/> set to <paramref name="value"/>.
        /// </summary>
        /// <param name="value">Value of <see cref="X"/>, <see cref="Y"/> and <see cref="Z"/>.</param>
        public PositionF(float value)
        {
            X = Y = Z = value;
        }

        /// <summary>
        /// Creates a new instance of <see cref="PositionF"/> with specific values.
        /// </summary>
        /// <param name="x">Value of X coordinate.</param>
        /// <param name="y">Value of Y coordinate.</param>
        /// <param name="z">Value of Z coordinate.</param>
        public PositionF(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>
        /// Creates a new instance of <see cref="PositionF"/> with specific values.
        /// </summary>
        /// <param name="x">Value of X coordinate.</param>
        /// <param name="y">Value of Y coordinate.</param>
        /// <param name="z">Value of Z coordinate.</param>
        public PositionF(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>
        /// Returns <see cref="PositionF"/> formatted as a <see cref="string"/>.
        /// </summary>
        /// <returns></returns>
        public override string ToString() => $"{X:0.0}:{Y:0.0}:{Z:0.0}";

        /// <summary>
        /// Calculates distance of this <see cref="PositionF"/> from <see cref="Zero"/>.
        /// </summary>
        public float Magnitude => MathF.Sqrt(X * X + Y * Y + Z * Z);

        /// <summary>
        /// Indicates whether this <see cref="PositionF"/> is near equal to <paramref name="other"/>.
        /// </summary>
        public bool Equals(PositionF other) => IsNear(X, other.X) && IsNear(Y, other.Y) && IsNear(Z, other.Z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsNear(float a, float b) => MathF.Abs(a - b) <= 0.01f;

        /// <summary>
        /// Truncates the decimal component of each part of this <see cref="PositionF"/>.
        /// </summary>
        public PositionF Floor() => new PositionF(MathF.Floor(X), MathF.Floor(Y), MathF.Floor(Z));

        /// <summary>
        /// Performs vector normalization on this <see cref="PositionF"/>'s coordinates.
        /// </summary>
        /// <returns>Normalized <see cref="PositionF"/>.</returns>
        public PositionF Normalize() => new PositionF(X / Magnitude, Y / Magnitude, Z / Magnitude);

        /// <summary>
        /// Returns <see cref="PositionF"/> clamped to the inclusive range of <paramref name="min"/> and <paramref name="max"/>.
        /// </summary>
        public PositionF Clamp(PositionF min, PositionF max) =>
            new PositionF(Math.Clamp(X, min.X, max.X), Math.Clamp(Y, min.Y, max.Y), Math.Clamp(Z, min.Z, max.Z));

        /// <summary>
        /// Returns <see cref="PositionF"/> clamped to fit inside a single minecraft chunk.
        /// </summary>
        public PositionF ChunkClamp() => Clamp(Zero, ChunkSize);

        /// <summary>
        /// Calculates the distance between two <see cref="PositionF"/> objects.
        /// </summary>
        public static double DistanceTo(PositionF from, PositionF to) => MathF.Sqrt(Square(to.X - from.X) + Square(to.Y - from.Y) + Square(to.Z - from.Z));

        /// <summary>
        /// Calculates the square of a <paramref name="number"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Square(float number) => number * number;

        public PositionF Add(float x, float y, float z)
        {
            X += x;
            Y += y;
            Z += z;

            return this;
        }

        public static PositionF Min(PositionF value1, PositionF value2) =>
            new PositionF(MathF.Min(value1.X, value2.X), MathF.Min(value1.Y, value2.Y), MathF.Min(value1.Z, value2.Z));

        public static PositionF Max(PositionF value1, PositionF value2) =>
            new PositionF(MathF.Max(value1.X, value2.X), MathF.Max(value1.Y, value2.Y), MathF.Max(value1.Z, value2.Z));

        public static PositionF Clamp(PositionF value, PositionF min, PositionF max) =>
            new PositionF(Math.Clamp(value.X, min.X, max.X), Math.Clamp(value.Y, min.Y, max.Y), Math.Clamp(value.Z, min.Z, max.Z));

        public static bool operator !=(PositionF a, PositionF b) => !a.Equals(b);

        public static bool operator ==(PositionF a, PositionF b) => a.Equals(b);

        public static PositionF operator +(PositionF a, PositionF b) => new PositionF(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static PositionF operator +(PositionF a, (int x, int y, int z) b) => new PositionF(a.X + b.x, a.Y + b.y, a.Z + b.z);

        public static PositionF operator -(PositionF a, PositionF b) => new PositionF(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static PositionF operator -(PositionF a) => new PositionF(-a.X, -a.Y, -a.Z);

        public static PositionF operator *(PositionF a, PositionF b) => new PositionF(a.X * b.X, a.Y * b.Y, a.Z * b.Z);

        public static PositionF operator /(PositionF a, PositionF b) => new PositionF(a.X / b.X, a.Y / b.Y, a.Z / b.Z);

        public static PositionF operator %(PositionF a, PositionF b) => new PositionF(a.X % b.X, a.Y % b.Y, a.Z % b.Z);

        public static PositionF operator +(PositionF a, float b) => new PositionF(a.X + b, a.Y + b, a.Z + b);

        public static PositionF operator -(PositionF a, float b) => new PositionF(a.X - b, a.Y - b, a.Z - b);

        public static PositionF operator *(PositionF a, float b) => new PositionF(a.X * b, a.Y * b, a.Z * b);

        public static PositionF operator /(PositionF a, float b) => new PositionF(a.X / b, a.Y / b, a.Z / b);

        public static PositionF operator %(PositionF a, float b) => new PositionF(a.X % b, a.Y % b, a.Y % b);

        public static PositionF operator +(float a, PositionF b) => new PositionF(a + b.X, a + b.Y, a + b.Z);

        public static PositionF operator -(float a, PositionF b) => new PositionF(a - b.X, a - b.Y, a - b.Z);

        public static PositionF operator *(float a, PositionF b) => new PositionF(a * b.X, a * b.Y, a * b.Z);

        public static PositionF operator /(float a, PositionF b) => new PositionF(a / b.X, a / b.Y, a / b.Z);

        public static PositionF operator %(float a, PositionF b) => new PositionF(a % b.X, a % b.Y, a % b.Y);

        public static explicit operator Position(PositionF positionF)
        {
            return new Position((int)positionF.X, (int)positionF.Y, (int)positionF.Z);
        }

        public override bool Equals(object obj)
        {
            return obj is PositionF position && Equals(position);
        }

        public override int GetHashCode() => HashCode.Combine(X, Y, Z);

        #region Constants
        private static readonly PositionF ChunkSize = new PositionF(15, 255, 15);

        public static readonly PositionF Zero = new PositionF(0);
        public static readonly PositionF One = new PositionF(1);

        public static readonly PositionF Up = new PositionF(0, 1, 0);
        public static readonly PositionF Down = new PositionF(0, -1, 0);
        public static readonly PositionF Left = new PositionF(-1, 0, 0);
        public static readonly PositionF Right = new PositionF(1, 0, 0);
        public static readonly PositionF Backwards = new PositionF(0, 0, -1);
        public static readonly PositionF Forwards = new PositionF(0, 0, 1);

        public static readonly PositionF East = new PositionF(1, 0, 0);
        public static readonly PositionF West = new PositionF(-1, 0, 0);
        public static readonly PositionF North = new PositionF(0, 0, -1);
        public static readonly PositionF South = new PositionF(0, 0, 1);
        #endregion Constants
    }
}
