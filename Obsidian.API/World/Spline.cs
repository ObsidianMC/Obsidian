namespace Obsidian.API.World;

/// <summary>
/// Multipoint cubic spline over a density function coordinate.
/// </summary>
/// <remarks>
/// All math is done in <see cref="float"/> like vanilla's CubicSpline; doing it in double shifts terrain
/// heights slightly. Points must be sorted by <see cref="SplinePoint.Location"/>.
/// </remarks>
public class Spline : ISpline
{
    public required IDensityFunction Coordinate { get; init; }

    public required SplinePoint[] Points { get; init; }

    public float MinValue => this.State.MinValue;

    public float MaxValue => this.State.MaxValue;

    private SplineState State => field ??= SplineState.Create(this.Coordinate, this.Points);

    public float Apply(double x, double y, double z)
    {
        var state = this.State;
        var locations = state.Locations;
        var derivatives = state.Derivatives;
        var values = state.Values;

        var coordinate = (float)this.Coordinate.GetValue(x, y, z);
        var index = FindIntervalStart(locations, coordinate);
        var lastIndex = locations.Length - 1;

        if (index < 0)
            return LinearExtend(coordinate, locations, values[0].Apply(x, y, z), derivatives, 0);

        if (index == lastIndex)
            return LinearExtend(coordinate, locations, values[lastIndex].Apply(x, y, z), derivatives, lastIndex);

        var start = locations[index];
        var end = locations[index + 1];
        var t = (coordinate - start) / (end - start);

        var valueStart = values[index].Apply(x, y, z);
        var valueEnd = values[index + 1].Apply(x, y, z);

        var slopeStart = derivatives[index] * (end - start) - (valueEnd - valueStart);
        var slopeEnd = -derivatives[index + 1] * (end - start) + (valueEnd - valueStart);

        return Lerp(t, valueStart, valueEnd) + t * (1.0f - t) * Lerp(t, slopeStart, slopeEnd);
    }

    ISpline ISpline.MapAll(IDensityFunctionVisitor visitor) => this.MapAll(visitor);

    /// <inheritdoc cref="ISpline.MapAll"/>
    public Spline MapAll(IDensityFunctionVisitor visitor) => new()
    {
        Coordinate = visitor.Map(this.Coordinate),
        Points = Array.ConvertAll(this.Points, point => point with { Value = point.Value.MapAll(visitor) })
    };

    private static float Lerp(float t, float start, float end) => start + t * (end - start);

    private static float LinearExtend(float coordinate, float[] locations, float value, float[] derivatives, int index)
    {
        var derivative = derivatives[index];
        return derivative == 0.0f ? value : value + derivative * (coordinate - locations[index]);
    }

    /// <summary>
    /// Index of the last location less than or equal to <paramref name="coordinate"/>, or -1.
    /// </summary>
    private static int FindIntervalStart(float[] locations, float coordinate)
    {
        var low = 0;
        var high = locations.Length;

        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (coordinate < locations[mid])
                high = mid;
            else
                low = mid + 1;
        }

        return low - 1;
    }

    private sealed record SplineState(float[] Locations, float[] Derivatives, ISpline[] Values, float MinValue, float MaxValue)
    {
        public static SplineState Create(IDensityFunction coordinate, SplinePoint[] points)
        {
            var locations = Array.ConvertAll(points, point => (float)point.Location);
            var derivatives = Array.ConvertAll(points, point => (float)point.Derivative);
            var values = Array.ConvertAll(points, point => point.Value);
            var lastIndex = locations.Length - 1;

            var min = float.PositiveInfinity;
            var max = float.NegativeInfinity;
            var coordinateMin = (float)coordinate.MinValue;
            var coordinateMax = (float)coordinate.MaxValue;

            if (coordinateMin < locations[0])
            {
                var extendedMin = LinearExtend(coordinateMin, locations, values[0].MinValue, derivatives, 0);
                var extendedMax = LinearExtend(coordinateMin, locations, values[0].MaxValue, derivatives, 0);
                min = Math.Min(min, Math.Min(extendedMin, extendedMax));
                max = Math.Max(max, Math.Max(extendedMin, extendedMax));
            }

            if (coordinateMax > locations[lastIndex])
            {
                var extendedMin = LinearExtend(coordinateMax, locations, values[lastIndex].MinValue, derivatives, lastIndex);
                var extendedMax = LinearExtend(coordinateMax, locations, values[lastIndex].MaxValue, derivatives, lastIndex);
                min = Math.Min(min, Math.Min(extendedMin, extendedMax));
                max = Math.Max(max, Math.Max(extendedMin, extendedMax));
            }

            foreach (var value in values)
            {
                min = Math.Min(min, value.MinValue);
                max = Math.Max(max, value.MaxValue);
            }

            for (var i = 0; i < lastIndex; i++)
            {
                var span = locations[i + 1] - locations[i];
                var (startMin, startMax) = (values[i].MinValue, values[i].MaxValue);
                var (endMin, endMax) = (values[i + 1].MinValue, values[i + 1].MaxValue);
                var (derivativeStart, derivativeEnd) = (derivatives[i], derivatives[i + 1]);

                if (derivativeStart == 0.0f && derivativeEnd == 0.0f)
                    continue;

                var slopeStart = derivativeStart * span;
                var slopeEnd = derivativeEnd * span;
                var lowest = Math.Min(startMin, endMin);
                var highest = Math.Max(startMax, endMax);
                var lowDelta = Math.Min(slopeStart - endMax + startMin, -slopeEnd + endMin - startMax);
                var highDelta = Math.Max(slopeStart - endMin + startMax, -slopeEnd + endMax - startMin);

                min = Math.Min(min, lowest + 0.25f * lowDelta);
                max = Math.Max(max, highest + 0.25f * highDelta);
            }

            return new(locations, derivatives, values, min, max);
        }
    }
}
