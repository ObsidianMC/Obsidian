using Obsidian.API.Noise;
using Obsidian.API.World.Generator.DensityFunctions;

namespace Obsidian.API.World;
public class Spline : ISpline
{
    public required IDensityFunction Coordinate { get; init; }

    public required SplinePoint[] Points { get; init; }

    public double MinValue { 
        get
        {
            if (!_created)
            {
                Create();
            }
            return field;
        }
        private set; 
    }

    public double MaxValue
    {
        get
        {
            if (!_created)
            {
                Create();
            }
            return field;
        }
        private set;
    }

    private bool _created = false;
    private List<double> _locations;
    private List<double> _derivatives;
    private List<ISpline> _values;
    private int _lastIndex;

    public void Create()
    {
        _created = true;
        MaxValue = double.MaxValue;
        MinValue = double.MinValue;
        _locations = Points.Select(x => x.Location).ToList();
        _derivatives = Points.Select(x => x.Derivative).ToList();
        _values = Points.Select(x => x.Value).ToList();
        _lastIndex = _locations.Count - 1;

        var coordMin = Coordinate.MinValue;
        var coordMax = Coordinate.MaxValue;

        // Search for min/max spline values
        if (coordMin < _locations[0])
        {
            var extendedMin = LinearExtend(coordMin, _values[0].MinValue, _lastIndex);
            var extendedMax = LinearExtend(coordMin, _values[0].MaxValue, _lastIndex);
            MinValue = Math.Min(MinValue, Math.Min(extendedMin, extendedMax));
            MaxValue = Math.Max(MaxValue, Math.Max(extendedMin, extendedMax));
        }

        if (coordMax > _locations[_lastIndex])
        {
            var extendedMin = LinearExtend(coordMax, _values[_lastIndex].MinValue, _lastIndex);
            var extendedMax = LinearExtend(coordMax, _values[_lastIndex].MaxValue, _lastIndex);
            MinValue = Math.Min(MinValue, Math.Min(extendedMin, extendedMax));
            MaxValue = Math.Max(MaxValue, Math.Max(extendedMin, extendedMax));
        }

        foreach (var spline in _values)
        {
            MinValue = Math.Min(MinValue, spline.MinValue);
            MaxValue = Math.Max(MaxValue, spline.MaxValue);
        }

        for (int i = 0; i < _lastIndex; i++)
        {
            var startLocation = _locations[i];
            var endLocation = _locations[i + 1];
            var span = endLocation - startLocation;
            var startSpline = _values[i];
            var endSpline = _values[i + 1];

            var derivativeStart = _derivatives[i];
            var derivativeEnd = _derivatives[i + 1];

            if (derivativeStart != 0.0f || derivativeEnd != 0.0f)
            {
                var deltaMin = derivativeStart * span - endSpline.MaxValue + startSpline.MinValue;
                var deltaMax = -derivativeEnd * span + endSpline.MinValue - startSpline.MaxValue;

                MinValue = Math.Min(MinValue, startSpline.MinValue + 0.25f * Math.Min(deltaMin, deltaMax));
                MaxValue = Math.Max(MaxValue, startSpline.MaxValue + 0.25f * Math.Max(deltaMin, deltaMax));
            }
        }
    }

    public double Apply(double x, double y, double z)
    {
        var coordValue = Coordinate.GetValue(x, y, z);
        int intervalIndex = FindIntervalStart(coordValue);

        if (intervalIndex < 0)
        {
            return LinearExtend(coordValue, _values[0].Apply(x, y, z), 0);
        }
        else if (intervalIndex == _lastIndex)
        {
            return LinearExtend(coordValue, _values[_lastIndex].Apply(x, y, z), _lastIndex);
        }
        else
        {
            var start = _locations[intervalIndex];
            var end = _locations[intervalIndex + 1];
            var t = (coordValue - start) / (end - start);

            var valueStart = _values[intervalIndex].Apply(x, y, z);
            var valueEnd = _values[intervalIndex + 1].Apply(x, y, z);

            var derivStart = _derivatives[intervalIndex] * (end - start) - (valueEnd - valueStart);
            var derivEnd = -_derivatives[intervalIndex + 1] * (end - start) + (valueEnd - valueStart);

            return MathUtils.Lerp(t, valueStart, valueEnd) + t * (1.0 - t) * MathUtils.Lerp(t, derivStart, derivEnd);
        }
    }

    private double LinearExtend(double coord, double val, int index) => _derivatives[index] == 0 ? val : val + _derivatives[index] * (coord - _locations[index]);

    private int FindIntervalStart(double coordinate)
    {
        int index = Array.BinarySearch(_locations.ToArray(), coordinate);
        return index < 0 ? ~index - 1 : index;
    }
}
