using System;

namespace Obsidian.Util.DataTypes
{
    public class Position : IEquatable<Position>
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public bool Opt { get; set; }

        public Position() { }

        public Position(double value, bool opt = false)
        {
            X = Y = Z = value;
            this.Opt = opt;
        }

        public Position(int x, int y, int z, bool opt = false)
        {
            this.X = x;
            this.Y = y;
            this.Z = z;
            this.Opt = opt;
        }

        public Position(double x, double y, double z, bool opt = false)
        {
            this.X = x;
            this.Y = y;
            this.Z = z;
            this.Opt = opt;
        }

        public bool Match(int x, int y, int z)
        {
            return this.X == x && this.Y == y && this.Z == z;
        }

        public override string ToString()
        {
            return $"{X}:{Y}:{Z}";
        }


        /// <summary>
        ///     Finds the distance of this vector from Position.Zero
        /// </summary>
        public double Distance
        {
            get { return DistanceTo(Zero); }
        }

        public bool Equals(Position other)
        {
            return other.X.Equals(X) && other.Y.Equals(Y) && other.Z.Equals(Z);
        }

        /// <summary>
        ///     Truncates the decimal component of each part of this Position.
        /// </summary>
        public Position Floor()
        {
            return new Position(Math.Floor(X), Math.Floor(Y), Math.Floor(Z));
        }

        public Position Normalize()
        {
            return new Position(X / Distance, Y / Distance, Z / Distance);
        }

        /// <summary>
        ///     Calculates the distance between two Position objects.
        /// </summary>
        public double DistanceTo(Position other)
        {
            return Math.Sqrt(Square(other.X - X) +
                             Square(other.Y - Y) +
                             Square(other.Z - Z));
        }

        /// <summary>
        ///     Calculates the square of a num.
        /// </summary>
        private double Square(double num)
        {
            return num * num;
        }

        public Position Add(double x, double y, double z)
        {
            this.X += x;
            this.Y += y;
            this.Z += z;

            return this;
        }

        public static Position Min(Position value1, Position value2)
        {
            return new Position(
                Math.Min(value1.X, value2.X),
                Math.Min(value1.Y, value2.Y),
                Math.Min(value1.Z, value2.Z));
        }

        public static Position Max(Position value1, Position value2)
        {
            return new Position(
                Math.Max(value1.X, value2.X),
                Math.Max(value1.Y, value2.Y),
                Math.Max(value1.Z, value2.Z));
        }

        public static bool operator !=(Position a, Position b)
        {
            return !a.Equals(b);
        }

        public static bool operator ==(Position a, Position b)
        {
            return a.Equals(b);
        }

        public static Position operator +(Position a, Position b)
        {
            return new Position(
                a.X + b.X,
                a.Y + b.Y,
                a.Z + b.Z);
        }

        public static Position operator -(Position a, Position b)
        {
            return new Position(
                a.X - b.X,
                a.Y - b.Y,
                a.Z - b.Z);
        }

        public static Position operator -(Position a)
        {
            return new Position(
                -a.X,
                -a.Y,
                -a.Z);
        }

        public static Position operator *(Position a, Position b)
        {
            return new Position(
                a.X * b.X,
                a.Y * b.Y,
                a.Z * b.Z);
        }

        public static Position operator /(Position a, Position b)
        {
            return new Position(
                a.X / b.X,
                a.Y / b.Y,
                a.Z / b.Z);
        }

        public static Position operator %(Position a, Position b)
        {
            return new Position(a.X % b.X, a.Y % b.Y, a.Z % b.Z);
        }

        public static Position operator +(Position a, double b)
        {
            return new Position(
                a.X + b,
                a.Y + b,
                a.Z + b);
        }

        public static Position operator -(Position a, double b)
        {
            return new Position(
                a.X - b,
                a.Y - b,
                a.Z - b);
        }

        public static Position operator *(Position a, double b)
        {
            return new Position(
                a.X * b,
                a.Y * b,
                a.Z * b);
        }

        public static Position operator /(Position a, double b)
        {
            return new Position(
                a.X / b,
                a.Y / b,
                a.Z / b);
        }

        public static Position operator %(Position a, double b)
        {
            return new Position(a.X % b, a.Y % b, a.Y % b);
        }

        public static Position operator +(double a, Position b)
        {
            return new Position(
                a + b.X,
                a + b.Y,
                a + b.Z);
        }

        public static Position operator -(double a, Position b)
        {
            return new Position(
                a - b.X,
                a - b.Y,
                a - b.Z);
        }

        public static Position operator *(double a, Position b)
        {
            return new Position(
                a * b.X,
                a * b.Y,
                a * b.Z);
        }

        public static Position operator /(double a, Position b)
        {
            return new Position(
                a / b.X,
                a / b.Y,
                a / b.Z);
        }

        public static Position operator %(double a, Position b)
        {
            return new Position(a % b.X, a % b.Y, a % b.Y);
        }

        public override bool Equals(object obj)
        {
            if (obj is null)
                return false;
            if (obj.GetType() != typeof(Position))
                return false;
            return Equals((Position)obj);
        }

        public override int GetHashCode() => HashCode.Combine(this.X, this.Y, this.Z);

        #region Constants

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

    public class SoundPosition
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }

        public SoundPosition() { }

        public SoundPosition(int x, int y, int z)
        {
            this.X = (int)((x * 8) / 32.0D);
            this.Y = (int)((y * 8) / 32.0D);
            this.Z = (int)((z * 8) / 32.0D);
        }

        public SoundPosition(double x, double y, double z)
        {
            this.X = (int)((x * 8) / 32.0D);
            this.Y = (int)((y * 8) / 32.0D);
            this.Z = (int)((z * 8) / 32.0D);
        }

        public bool Match(int x, int y, int z) => this.X == x && this.Y == y && this.Z == z;

        public override string ToString() => $"{X}:{Y}:{Z}";
    }
}
