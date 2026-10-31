namespace Obsidian.WorldData.Generators.Density;

/// <summary>
/// A cubic spline of 26.3's <c>spline</c> function: a constant, or points along a coordinate function, each with a
/// location, a value (a spline itself) and a derivative.
/// </summary>
/// <remarks>Evaluated in float with vanilla's CubicSpline formulas and operation order.</remarks>
internal abstract class CubicSpline
{
    /// <summary>The spline's value, reading its coordinate functions from <paramref name="coordinates"/>.</summary>
    public abstract float Evaluate(in SplineInput coordinates);

    /// <summary>Adds the coordinate functions of the spline and its points' splines to a set.</summary>
    public abstract void CollectCoordinates(HashSet<DensityFunction> coordinates);
}

internal sealed class ConstantSpline(float value) : CubicSpline
{
    public override float Evaluate(in SplineInput coordinates) => value;

    public override void CollectCoordinates(HashSet<DensityFunction> coordinates)
    {
    }
}

internal sealed class MultipointSpline(
    DensityFunction coordinate,
    ImmutableArray<float> locations,
    ImmutableArray<CubicSpline> values,
    ImmutableArray<float> derivatives) : CubicSpline
{
    public override float Evaluate(in SplineInput coordinates)
    {
        var input = coordinates.Value(coordinate);
        var start = this.IntervalStart(input);
        var last = locations.Length - 1;

        if (start < 0)
            return this.ExtendLinearly(input, values[0].Evaluate(coordinates), 0);

        if (start == last)
            return this.ExtendLinearly(input, values[last].Evaluate(coordinates), last);

        var x1 = locations[start];
        var x2 = locations[start + 1];
        var t = (input - x1) / (x2 - x1);

        var y1 = values[start].Evaluate(coordinates);
        var y2 = values[start + 1].Evaluate(coordinates);
        var a = derivatives[start] * (x2 - x1) - (y2 - y1);
        var b = -derivatives[start + 1] * (x2 - x1) + (y2 - y1);

        var straight = FloatMath.Lerp(t, y1, y2);
        var curve = t * (1.0f - t) * FloatMath.Lerp(t, a, b);

        return straight + curve;
    }

    public override void CollectCoordinates(HashSet<DensityFunction> coordinates)
    {
        coordinates.Add(coordinate);

        foreach (var value in values)
            value.CollectCoordinates(coordinates);
    }

    // The point before the first location above the input, or the last point (also for NaN), like vanilla's binary
    // search over the ascending locations.
    private int IntervalStart(float input)
    {
        for (var i = 0; i < locations.Length; i++)
        {
            if (input < locations[i])
                return i - 1;
        }

        return locations.Length - 1;
    }

    private float ExtendLinearly(float input, float value, int index)
    {
        var derivative = derivatives[index];
        return derivative == 0.0f ? value : value + derivative * (input - locations[index]);
    }
}

/// <summary>
/// The values of a <see cref="SplineFunction"/>'s coordinate functions at one position, without allocating: sampled
/// once each when first needed at a point, like vanilla's PointSplineInput, or copied from the volume buffers.
/// </summary>
internal readonly ref struct SplineInput
{
    private readonly ReadOnlySpan<DensityFunction> coordinates;
    private readonly Span<float> values;
    private readonly Span<bool> sampled;
    private readonly int x;
    private readonly int y;
    private readonly int z;

    /// <summary>
    /// An input for a position. <paramref name="values"/> and <paramref name="sampled"/> hold one entry per coordinate
    /// function; a coordinate not yet <paramref name="sampled"/> is sampled at the position when the spline reads it.
    /// </summary>
    public SplineInput(
        ReadOnlySpan<DensityFunction> coordinates,
        Span<float> values,
        Span<bool> sampled,
        int x,
        int y,
        int z)
    {
        this.coordinates = coordinates;
        this.values = values;
        this.sampled = sampled;
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public float Value(DensityFunction coordinate)
    {
        var index = this.IndexOf(coordinate);

        if (!this.sampled[index])
        {
            this.values[index] = coordinate.Sample(this.x, this.y, this.z);
            this.sampled[index] = true;
        }

        return this.values[index];
    }

    // A spline has a handful of coordinates, so a linear search beats hashing.
    private int IndexOf(DensityFunction coordinate)
    {
        for (var i = 0; i < this.coordinates.Length; i++)
        {
            if (ReferenceEquals(this.coordinates[i], coordinate))
                return i;
        }

        throw new ArgumentException("The function isn't one of the spline's coordinates.", nameof(coordinate));
    }
}

/// <summary>
/// <c>spline</c>: a <see cref="CubicSpline"/> of other density functions. Over a volume each coordinate function is
/// sampled as a volume, like vanilla's SplineFunction.
/// </summary>
internal sealed class SplineFunction : DensityFunction
{
    // Above this many coordinates (vanilla's splines have up to 4) their values go on the heap instead of the stack.
    private const int MaxStackCoordinates = 32;

    private readonly CubicSpline spline;
    private readonly DensityFunction[] coordinates;

    public SplineFunction(CubicSpline spline)
    {
        var coordinates = new HashSet<DensityFunction>(ReferenceEqualityComparer.Instance);
        spline.CollectCoordinates(coordinates);

        this.spline = spline;
        this.coordinates = [.. coordinates];
    }

    public override bool IsFlat => this.coordinates.All(coordinate => coordinate.IsFlat);

    public override float Sample(int x, int y, int z)
    {
        var count = this.coordinates.Length;
        var onStack = count <= MaxStackCoordinates;
        Span<float> values = onStack ? stackalloc float[count] : new float[count];
        Span<bool> sampled = onStack ? stackalloc bool[count] : new bool[count];

        var input = new SplineInput(this.coordinates, values, sampled, x, y, z);

        return this.spline.Evaluate(input);
    }

    public override void SampleVolume(Span<float> output, in DensityVolume volume)
    {
        var count = this.coordinates.Length;
        var onStack = count <= MaxStackCoordinates;
        Span<float> values = onStack ? stackalloc float[count] : new float[count];
        Span<bool> sampled = onStack ? stackalloc bool[count] : new bool[count];

        // Every value is copied from the buffers, so the input never samples a coordinate and needs no position.
        sampled.Fill(true);
        var input = new SplineInput(this.coordinates, values, sampled, 0, 0, 0);

        var buffers = new DensityBuffer[count];
        try
        {
            for (var i = 0; i < count; i++)
                buffers[i] = DensityBuffer.Sample(this.coordinates[i], volume);

            for (var index = 0; index < output.Length; index++)
            {
                for (var i = 0; i < count; i++)
                    values[i] = buffers[i].Span[index];

                output[index] = this.spline.Evaluate(input);
            }
        }
        finally
        {
            foreach (var buffer in buffers)
                buffer.Dispose();
        }
    }
}
