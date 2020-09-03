using System;
using System.Collections.Generic;

namespace Obsidian.Util.DataTypes
{
    public enum ContainmentType
    {
        Disjoint,
        Contains,
        Intersects
    }

    public struct BoundingBox : IEquatable<BoundingBox>
    {
        public const int CornerCount = 8;
        public Position Max;
        public Position Min;

        public BoundingBox(Position min, Position max)
        {
            this.Min = min;
            this.Max = max;
        }

        public double Height
        {
            get => Max.Y - Min.Y;
        }

        public double Width
        {
            get => Max.X - Min.X;
        }

        public double Depth
        {
            get => Max.Z - Min.Z;
        }

        public bool Equals(BoundingBox other) => (Min == other.Min) && (Max == other.Max);

        public ContainmentType Contains(BoundingBox box)
        {
            //test if all corner is in the same side of a face by just checking min and max
            if (box.Max.X < Min.X
                || box.Min.X > Max.X
                || box.Max.Y < Min.Y
                || box.Min.Y > Max.Y
                || box.Max.Z < Min.Z
                || box.Min.Z > Max.Z)
                return ContainmentType.Disjoint;

            if (box.Min.X >= Min.X
                && box.Max.X <= Max.X
                && box.Min.Y >= Min.Y
                && box.Max.Y <= Max.Y
                && box.Min.Z >= Min.Z
                && box.Max.Z <= Max.Z)
                return ContainmentType.Contains;

            return ContainmentType.Intersects;
        }

        public bool Contains(Position vec)
        {
            return Min.X <= vec.X && vec.X <= Max.X &&
                   Min.Y <= vec.Y && vec.Y <= Max.Y &&
                   Min.Z <= vec.Z && vec.Z <= Max.Z;
        }

        public static BoundingBox CreateFromPoints(IEnumerable<Position> points)
        {
            if (points == null)
                throw new ArgumentNullException();

            var empty = true;
            var pos2 = new Position(float.MaxValue);
            var pos1 = new Position(float.MinValue);

            foreach (var Position in points)
            {
                pos2 = Position.Min(pos2, Position);
                pos1 = Position.Max(pos1, Position);
                empty = false;
            }

            if (empty)
                throw new ArgumentException();

            return new BoundingBox(pos2, pos1);
        }

        public BoundingBox OffsetBy(Position offset) => new BoundingBox(Min + offset, Max + offset);

        public Position[] GetCorners()
        {
            return new[]
            {
                new Position(Min.X, Max.Y, Max.Z),
                new Position(Max.X, Max.Y, Max.Z),
                new Position(Max.X, Min.Y, Max.Z),
                new Position(Min.X, Min.Y, Max.Z),
                new Position(Min.X, Max.Y, Min.Z),
                new Position(Max.X, Max.Y, Min.Z),
                new Position(Max.X, Min.Y, Min.Z),
                new Position(Min.X, Min.Y, Min.Z)
            };
        }

        public override bool Equals(object obj) => (obj is BoundingBox box) && this.Equals(box);

        public override int GetHashCode() => Min.GetHashCode() + Max.GetHashCode();

        public bool Intersects(BoundingBox box)
        {
            this.Intersects(ref box, out bool result);
            return result;
        }

        public void Intersects(ref BoundingBox box, out bool result)
        {
            if ((Max.X >= box.Min.X) && (Min.X <= box.Max.X))
            {
                if ((Max.Y < box.Min.Y) || (Min.Y > box.Max.Y))
                {
                    result = false;
                    return;
                }

                result = (Max.Z >= box.Min.Z) && (Min.Z <= box.Max.Z);
                return;
            }

            result = false;
        }

        public static BoundingBox operator +(BoundingBox a, double b) => new BoundingBox(a.Min - b, a.Max + b);
        public static bool operator ==(BoundingBox a, BoundingBox b) => a.Equals(b);

        public static bool operator !=(BoundingBox a, BoundingBox b) => !a.Equals(b);

        public override string ToString() => $"{{Min:{Min} Max:{Max}}}";
    }
}