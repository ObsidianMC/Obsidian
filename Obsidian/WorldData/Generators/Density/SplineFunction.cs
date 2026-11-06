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

    /// <summary>
    /// A copy of the spline that reads each coordinate function by its index in <paramref name="coordinates"/>, which
    /// holds every function <see cref="CollectCoordinates"/> adds. Only a bound spline can be evaluated.
    /// </summary>
    public abstract CubicSpline Bind(IReadOnlyList<DensityFunction> coordinates);
}

internal sealed class ConstantSpline(float value) : CubicSpline
{
    public override float Evaluate(in SplineInput coordinates) => value;

    public override void CollectCoordinates(HashSet<DensityFunction> coordinates)
    {
    }

    public override CubicSpline Bind(IReadOnlyList<DensityFunction> coordinates) => this;
}

/// <param name="coordinateIndex">The coordinate function's index in the input, once bound (see
/// <see cref="CubicSpline.Bind"/>); -1 before.</param>
internal sealed class MultipointSpline(
    DensityFunction coordinate,
    ImmutableArray<float> locations,
    ImmutableArray<CubicSpline> values,
    ImmutableArray<float> derivatives,
    int coordinateIndex = -1) : CubicSpline
{
    public override float Evaluate(in SplineInput coordinates)
    {
        var input = coordinates.Value(coordinateIndex);
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

    public override CubicSpline Bind(IReadOnlyList<DensityFunction> coordinates)
    {
        var index = -1;

        for (var i = 0; i < coordinates.Count; i++)
        {
            if (ReferenceEquals(coordinates[i], coordinate))
                index = i;
        }

        if (index < 0)
            throw new ArgumentException("The spline's coordinate isn't among the coordinates.", nameof(coordinates));

        var bound = values.Select(value => value.Bind(coordinates)).ToImmutableArray();

        return new MultipointSpline(coordinate, locations, bound, derivatives, index);
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
/// The values of a <see cref="SplineFunction"/>'s coordinate functions at one position, by their indices, without
/// allocating: sampled once each when first needed at a point, like vanilla's PointSplineInput, or read from the
/// volume buffers.
/// </summary>
internal readonly ref struct SplineInput
{
    private readonly ReadOnlySpan<DensityFunction> coordinates;
    private readonly Span<float> values;
    private readonly Span<bool> sampled;
    private readonly int x;
    private readonly int y;
    private readonly int z;

    // Over a volume: how far apart the coordinates' buffers are in values, and the position's index in each; 0 at a
    // point.
    private readonly int stride;
    private readonly int index;

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

    /// <summary>
    /// An input for the volume position <paramref name="index"/>, whose coordinates were sampled over the volume into
    /// <paramref name="values"/>, one coordinate's <paramref name="size"/> values after another.
    /// </summary>
    public SplineInput(Span<float> values, int size, int index)
    {
        this.values = values;
        this.stride = size;
        this.index = index;
    }

    /// <summary>The value of the coordinate function at <paramref name="coordinate"/> in the spline's list.</summary>
    public float Value(int coordinate)
    {
        if (this.stride > 0)
            return this.values[coordinate * this.stride + this.index];

        if (!this.sampled[coordinate])
        {
            this.values[coordinate] = this.coordinates[coordinate].Sample(this.x, this.y, this.z);
            this.sampled[coordinate] = true;
        }

        return this.values[coordinate];
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
    private readonly bool flat;

    public SplineFunction(CubicSpline spline)
    {
        var coordinates = new HashSet<DensityFunction>(ReferenceEqualityComparer.Instance);
        spline.CollectCoordinates(coordinates);

        // Bound, so that evaluating reads each coordinate by index rather than searching for it.
        this.coordinates = [.. coordinates];
        this.spline = spline.Bind(this.coordinates);
        this.flat = this.coordinates.All(coordinate => coordinate.IsFlat);
    }

    public override bool IsFlat => this.flat;

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
        // Data packs' terrain splines are often sampled uncached over cell corners, where each column has one value.
        if (this.flat && volume.SizeY > 1)
        {
            this.SampleLayer(output, volume);
            return;
        }

        var count = this.coordinates.Length;

        // One buffer holds every coordinate's values over the volume, one coordinate after another.
        var size = volume.Size;
        using var buffer = new DensityBuffer(count * size);
        var coordinateValues = buffer.Span;

        for (var i = 0; i < count; i++)
            this.coordinates[i].SampleVolume(coordinateValues.Slice(i * size, size), volume);

        for (var index = 0; index < output.Length; index++)
            output[index] = this.spline.Evaluate(new SplineInput(coordinateValues, size, index));
    }
}
