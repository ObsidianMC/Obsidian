namespace Obsidian.API;

/// <summary>An axis-aligned box in world space, given by its lowest and highest corners.</summary>
public readonly struct BoundingBox : IEquatable<BoundingBox>
{
    /// <summary>
    /// The number of corners a box has, and the length of the array <see cref="GetCorners"/> returns.
    /// </summary>
    public const int CornerCount = 8;

    /// <summary>The corner with the highest X, Y and Z.</summary>
    public readonly VectorD Max;
    /// <summary>The corner with the lowest X, Y and Z.</summary>
    public readonly VectorD Min;

    /// <summary>Creates a box from its lowest and highest corners.</summary>
    public BoundingBox(VectorD min, VectorD max)
    {
        this.Min = min;
        this.Max = max;
    }

    /// <summary>Gets the box's size along the Y axis, in blocks.</summary>
    public double Height
    {
        get => Max.Y - Min.Y;
    }

    /// <summary>Gets the box's size along the X axis, in blocks.</summary>
    public double Width
    {
        get => Max.X - Min.X;
    }

    /// <summary>Gets the box's size along the Z axis, in blocks.</summary>
    public double Depth
    {
        get => Max.Z - Min.Z;
    }

    /// <summary>Indicates whether this box has the same corners as <paramref name="other"/>.</summary>
    public bool Equals(BoundingBox other) => (Min == other.Min) && (Max == other.Max);

    /// <summary>
    /// Tests how <paramref name="box"/> relates to this box. Boxes that only touch at a face are not disjoint.
    /// </summary>
    /// <returns><see cref="ContainmentType.Contains"/> when <paramref name="box"/> lies fully inside this box,
    /// <see cref="ContainmentType.Disjoint"/> when they don't meet, and <see cref="ContainmentType.Intersects"/>
    /// otherwise.</returns>
    public ContainmentType Contains(BoundingBox box)
    {
        //test if all corner is in the same side of a face by just checking min and max
        if (box.Max.X < Min.X
            || box.Min.X > Max.X
            || box.Max.Y < Min.Y
            || box.Min.Y > Max.Y
            || box.Max.Z < Min.Z
            || box.Min.Z > Max.Z)
        {
            return ContainmentType.Disjoint;
        }

        if (box.Min.X >= Min.X
            && box.Max.X <= Max.X
            && box.Min.Y >= Min.Y
            && box.Max.Y <= Max.Y
            && box.Min.Z >= Min.Z
            && box.Max.Z <= Max.Z)
        {
            return ContainmentType.Contains;
        }

        return ContainmentType.Intersects;
    }

    /// <summary>Indicates whether a point lies inside the box or on its surface.</summary>
    public bool Contains(VectorD vec)
    {
        return Min.X <= vec.X && vec.X <= Max.X &&
               Min.Y <= vec.Y && vec.Y <= Max.Y &&
               Min.Z <= vec.Z && vec.Z <= Max.Z;
    }

    /// <summary>Creates the smallest box that holds every point.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="points"/> is empty.</exception>
    public static BoundingBox CreateFromPoints(IEnumerable<VectorD> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var empty = true;
        var pos2 = new VectorD(double.MaxValue);
        var pos1 = new VectorD(double.MinValue);

        foreach (var Position in points)
        {
            pos2 = VectorD.Min(pos2, Position);
            pos1 = VectorD.Max(pos1, Position);
            empty = false;
        }

        if (empty)
            throw new ArgumentException("Invalid points specified.");

        return new BoundingBox(pos2, pos1);
    }

    /// <summary>Gets a copy of this box moved by <paramref name="offset"/>.</summary>
    public BoundingBox OffsetBy(VectorD offset) => new BoundingBox(Min + offset, Max + offset);

    /// <summary>Gets the box's eight corners.</summary>
    public VectorD[] GetCorners()
    {
        return
        [
                new VectorD(Min.X, Max.Y, Max.Z),
                new VectorD(Max.X, Max.Y, Max.Z),
                new VectorD(Max.X, Min.Y, Max.Z),
                new VectorD(Min.X, Min.Y, Max.Z),
                new VectorD(Min.X, Max.Y, Min.Z),
                new VectorD(Max.X, Max.Y, Min.Z),
                new VectorD(Max.X, Min.Y, Min.Z),
                new VectorD(Min.X, Min.Y, Min.Z)
            ];
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => (obj is BoundingBox box) && this.Equals(box);

    /// <inheritdoc/>
    public override int GetHashCode() => Min.GetHashCode() + Max.GetHashCode();

    /// <summary>Indicates whether this box and <paramref name="box"/> overlap or touch.</summary>
    public bool Intersects(BoundingBox box)
    {
        this.Intersects(ref box, out bool result);
        return result;
    }

    /// <summary>Tests whether this box and <paramref name="box"/> overlap or touch, without copying the box.</summary>
    /// <param name="box">The box to test against.</param>
    /// <param name="result">Set to whether the boxes overlap or touch.</param>
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

    /// <summary>Grows the box by <paramref name="b"/> blocks on every side.</summary>
    public static BoundingBox operator +(BoundingBox a, float b) => new BoundingBox(a.Min - b, a.Max + b);
    /// <summary>Indicates whether two boxes have the same corners.</summary>
    public static bool operator ==(BoundingBox a, BoundingBox b) => a.Equals(b);

    /// <summary>Indicates whether two boxes differ in either corner.</summary>
    public static bool operator !=(BoundingBox a, BoundingBox b) => !a.Equals(b);

    /// <summary>Returns the box's corners as text, for debugging.</summary>
    public override string ToString() => $"{{Min:{Min} Max:{Max}}}";
}
