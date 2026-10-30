namespace Obsidian.WorldData.Generators.Density;

/// <summary>
/// A cubic spline of 26.3's <c>spline</c> function: a constant, or points along a coordinate function, each with a
/// location, a value (a spline itself) and a derivative.
/// </summary>
/// <remarks>Evaluated in float with vanilla's CubicSpline formulas and operation order.</remarks>
internal abstract class CubicSpline
{
    /// <summary>The spline's value, sampling its coordinate functions through <paramref name="coordinates"/>.</summary>
    public abstract float Evaluate(Func<DensityFunction, float> coordinates);

    /// <summary>Adds the coordinate functions of the spline and its points' splines to a set.</summary>
    public abstract void CollectCoordinates(HashSet<DensityFunction> coordinates);
}

internal sealed class ConstantSpline(float value) : CubicSpline
{
    public override float Evaluate(Func<DensityFunction, float> coordinates) => value;

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
    public override float Evaluate(Func<DensityFunction, float> coordinates)
    {
        var input = coordinates(coordinate);
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
/// <c>spline</c>: a <see cref="CubicSpline"/> of other density functions. Over a volume each coordinate function is
/// sampled as a volume, like vanilla's SplineFunction.
/// </summary>
internal sealed class SplineFunction : DensityFunction
{
    private readonly CubicSpline spline;
    private readonly DensityFunction[] coordinates;

    public SplineFunction(CubicSpline spline)
    {
        var coordinates = new HashSet<DensityFunction>(ReferenceEqualityComparer.Instance);
        spline.CollectCoordinates(coordinates);

        this.spline = spline;
        this.coordinates = [.. coordinates];
    }

    public override float Sample(int x, int y, int z) => this.spline.Evaluate(function => function.Sample(x, y, z));

    public override void SampleVolume(Span<float> output, in DensityVolume volume)
    {
        var buffers = new Dictionary<DensityFunction, DensityBuffer>(ReferenceEqualityComparer.Instance);
        try
        {
            foreach (var coordinate in this.coordinates)
                buffers[coordinate] = DensityBuffer.Sample(coordinate, volume);

            for (var i = 0; i < output.Length; i++)
            {
                var index = i;
                output[i] = this.spline.Evaluate(function => buffers[function].Span[index]);
            }
        }
        finally
        {
            foreach (var buffer in buffers.Values)
                buffer.Dispose();
        }
    }
}
