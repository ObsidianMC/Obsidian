using System;
using System.Runtime.CompilerServices;

namespace Obsidian.API
{
    /// <summary>
    /// Represents position in three-dimensional space. Uses <see cref="int"/>.
    /// </summary>
    public struct Position : IEquatable<Position>
    {
        /// <summary>
        /// X component of the <see cref="Position"/>.
        /// </summary>
        public int X { get; set; }
        /// <summary>
        /// Y component of the <see cref="Position"/>.
        /// </summary>
        public int Y { get; set; }
        /// <summary>
        /// Z component of the <see cref="Position"/>.
        /// </summary>
        public int Z { get; set; }

        /// <summary>
        /// Creates new instance of <see cref="Position"/> with <see cref="X"/>, <see cref="Y"/> and <see cref="Z"/> set to <paramref name="value"/>.
        /// </summary>
        /// <param name="value">Value of <see cref="X"/>, <see cref="Y"/> and <see cref="Z"/>.</param>
        public Position(int value)
        {
            X = Y = Z = value;
        }

        /// <summary>
        /// Creates a new instance of <see cref="Position"/> with specific values.
        /// </summary>
        /// <param name="x">Value of X coordinate.</param>
        /// <param name="y">Value of Y coordinate.</param>
        /// <param name="z">Value of Z coordinate.</param>
        public Position(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>
        /// Returns <see cref="Position"/> formatted as a <see cref="string"/>.
        /// </summary>
        /// <returns></returns>
        public override string ToString() => $"{X}:{Y}:{Z}";

        /// <summary>
        /// Calculates distance of this <see cref="Position"/> from <see cref="Zero"/>.
        /// </summary>
        public float Magnitude => MathF.Sqrt(X * X + Y * Y + Z * Z);

        /// <summary>
        /// Indicates whether this <see cref="Position"/> is near equal to <paramref name="other"/>.
        /// </summary>
        public bool Equals(Position other) => X == other.X && Y == other.Y && Z == other.Z;

        public SoundPosition SoundPosition => new SoundPosition(this.X, this.Y, this.Z);

        /// <summary>
        /// Returns <see cref="Position"/> clamped to the inclusive range of <paramref name="min"/> and <paramref name="max"/>.
        /// </summary>
        public Position Clamp(Position min, Position max) =>
            new Position(Math.Clamp(X, min.X, max.X), Math.Clamp(Y, min.Y, max.Y), Math.Clamp(Z, min.Z, max.Z));

        /// <summary>
        /// Returns <see cref="Position"/> clamped to fit inside a single minecraft chunk.
        /// </summary>
        public Position ChunkClamp() => Clamp(Zero, ChunkSize);

        /// <summary>
        /// Calculates the distance between two <see cref="Position"/> objects.
        /// </summary>
        public static double DistanceTo(Position from, Position to) => MathF.Sqrt(Square(to.X - from.X) + Square(to.Y - from.Y) + Square(to.Z - from.Z));

        /// <summary>
        /// Calculates the square of a <paramref name="number"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Square(int number) => number * number;

        public Position Add(int x, int y, int z)
        {
            X += x;
            Y += y;
            Z += z;

            return this;
        }

        public static Position Min(Position value1, Position value2) =>
            new Position(Math.Min(value1.X, value2.X), Math.Min(value1.Y, value2.Y), Math.Min(value1.Z, value2.Z));

        public static Position Max(Position value1, Position value2) =>
            new Position(Math.Max(value1.X, value2.X), Math.Max(value1.Y, value2.Y), Math.Max(value1.Z, value2.Z));

        public static Position Clamp(Position value, Position min, Position max) =>
            new Position(Math.Clamp(value.X, min.X, max.X), Math.Clamp(value.Y, min.Y, max.Y), Math.Clamp(value.Z, min.Z, max.Z));

        public static bool operator !=(Position a, Position b) => !a.Equals(b);

        public static bool operator ==(Position a, Position b) => a.Equals(b);

        public static Position operator +(Position a, Position b) => new Position(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Position operator +(Position a, (int x, int y, int z) b) => new Position(a.X + b.x, a.Y + b.y, a.Z + b.z);

        public static Position operator -(Position a, Position b) => new Position(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static Position operator -(Position a) => new Position(-a.X, -a.Y, -a.Z);

        public static Position operator *(Position a, Position b) => new Position(a.X * b.X, a.Y * b.Y, a.Z * b.Z);

        public static Position operator /(Position a, Position b) => new Position(a.X / b.X, a.Y / b.Y, a.Z / b.Z);

        public static Position operator %(Position a, Position b) => new Position(a.X % b.X, a.Y % b.Y, a.Z % b.Z);

        public static Position operator +(Position a, int b) => new Position(a.X + b, a.Y + b, a.Z + b);

        public static Position operator -(Position a, int b) => new Position(a.X - b, a.Y - b, a.Z - b);

        public static Position operator *(Position a, int b) => new Position(a.X * b, a.Y * b, a.Z * b);

        public static Position operator /(Position a, int b) => new Position(a.X / b, a.Y / b, a.Z / b);

        public static Position operator %(Position a, int b) => new Position(a.X % b, a.Y % b, a.Y % b);

        public static Position operator +(int a, Position b) => new Position(a + b.X, a + b.Y, a + b.Z);

        public static Position operator -(int a, Position b) => new Position(a - b.X, a - b.Y, a - b.Z);

        public static Position operator *(int a, Position b) => new Position(a * b.X, a * b.Y, a * b.Z);

        public static Position operator /(int a, Position b) => new Position(a / b.X, a / b.Y, a / b.Z);

        public static Position operator %(int a, Position b) => new Position(a % b.X, a % b.Y, a % b.Y);

        public static implicit operator PositionF(Position position)
        {
            return new PositionF(position.X, position.Y, position.Z);
        }

        public override bool Equals(object obj)
        {
            return obj is Position position && Equals(position);
        }

        public override int GetHashCode() => HashCode.Combine(X, Y, Z);

        #region Constants
        private static readonly Position ChunkSize = new Position(15, 255, 15);

        public static readonly Position Zero = new Position(0);
        public static readonly Position One = new Position(1);

        public static readonly Position Up = new Position(0, 1, 0);
        public static readonly Position Down = new Position(0, -1, 0);
        public static readonly Position Left = new Position(-1, 0, 0);
        public static readonly Position Right = new Position(1, 0, 0);
        public static readonly Position Backwards = new Position(0, 0, -1);
        public static readonly Position Forwards = new Position(0, 0, 1);

        public static readonly Position East = new Position(1, 0, 0);
        public static readonly Position West = new Position(-1, 0, 0);
        public static readonly Position North = new Position(0, 0, -1);
        public static readonly Position South = new Position(0, 0, 1);
        #endregion Constants
    }
}
