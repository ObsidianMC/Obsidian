namespace Obsidian.WorldData.Generators.Density;

/// <summary>
/// What a density function's values can be, through every way of sampling it: at a point, over a volume and at a
/// volume's selected positions. Every value that isn't NaN lies in [<see cref="Min"/>, <see cref="Max"/>] (with -0 and
/// +0 equal); a bound is infinite where the function may reach that infinity. -0 and NaN occur only where the flags
/// allow them.
/// </summary>
/// <remarks>
/// <para>
/// Functions work these out once, as the graph is built, from their inputs' bounds. The rules are conservative: when
/// a function can't tell, it reports <see cref="Unknown"/>, which proves nothing. They follow the float operations
/// the functions perform. IEEE rounding is monotonic, so where an operation is monotonic in each input (sums,
/// products, clamps), the same float operation on the inputs' bounds gives exact bounds of its results.
/// </para>
/// <para>
/// These are Obsidian's own, not vanilla's value ranges: they also track -0 and NaN, and only let a multiplication skip
/// work whose result is already known bit for bit (see <see cref="KeepsZeroProduct"/>). They change no value.
/// </para>
/// </remarks>
internal readonly record struct ValueBounds(float Min, float Max, bool NegativeZero, bool NaN)
{
    // Coordinates noises are sampled at stay within this, so that wrapping them keeps them inside the lattice's int
    // range. Real noises are far below it: block coordinates are ints and scales are near 1.
    private const double MaxNoiseCoordinate = 1L << 50;

    // A gradient noise octave is a blend of dot products of a gradient with two unit components and an offset inside
    // its cell, so it stays within ±2. The slack covers the float rounding of the fade curve and the three lerps, which
    // can overshoot by a few ulps.
    private const double OctaveBound = 2.0 * 1.001;

    /// <summary>Nothing is known: any value, -0 or NaN.</summary>
    public static ValueBounds Unknown { get; } = new(float.NegativeInfinity, float.PositiveInfinity, true, true);

    /// <summary>
    /// Whether multiplying any zero by any of these values gives back that zero bit for bit: the values are finite, at
    /// least +0, and never -0 or NaN. Then +0 times a value is +0 and -0 times a value is -0, so a product needn't
    /// sample this side where the other is zero.
    /// </summary>
    /// <remarks>
    /// The other values break it: +0 times a negative value or -0 is -0, and a zero times an infinity or NaN is NaN.
    /// </remarks>
    public bool KeepsZeroProduct => !this.NaN && !this.NegativeZero && this.Min >= 0.0f && float.IsFinite(this.Max);

    /// <summary>Whether no value is NaN or infinite.</summary>
    public bool IsFinite => !this.NaN && float.IsFinite(this.Min) && float.IsFinite(this.Max);

    private bool ContainsZero => this.Min <= 0.0f && this.Max >= 0.0f;

    // Whether a value can have its sign bit set: a negative value, -0, or NaN (whose sign isn't known).
    private bool CanBeNegativeSigned => this.Min < 0.0f || this.NegativeZero || this.NaN;

    // Whether a value can have its sign bit clear: a positive value, +0, or NaN.
    private bool CanBePositiveSigned => this.Max >= 0.0f || this.NaN;

    /// <summary>A single value.</summary>
    public static ValueBounds Of(float value)
    {
        if (float.IsNaN(value))
            return Unknown;

        var negativeZero = IsNegativeZero(value);

        return new ValueBounds(value, value, negativeZero, false);
    }

    /// <summary>The values either of two bounds allows, as a choice between two functions gives.</summary>
    public ValueBounds Union(ValueBounds other) => new(
        MathF.Min(this.Min, other.Min),
        MathF.Max(this.Max, other.Max),
        this.NegativeZero || other.NegativeZero,
        this.NaN || other.NaN);

    /// <summary>The float sum of two values.</summary>
    public static ValueBounds Add(ValueBounds left, ValueBounds right)
    {
        // An infinity on each side can cancel to NaN.
        if (!left.IsFinite || !right.IsFinite)
            return Unknown;

        // A sum is -0 only when both terms are -0: an exact cancellation rounds to +0.
        var negativeZero = left.NegativeZero && right.NegativeZero;

        return new ValueBounds(left.Min + right.Min, left.Max + right.Max, negativeZero, false);
    }

    /// <summary>The float product of two values.</summary>
    public static ValueBounds Multiply(ValueBounds left, ValueBounds right)
    {
        // A zero times an infinity is NaN.
        if (!left.IsFinite || !right.IsFinite)
            return Unknown;

        // A product is extreme at a corner of the two ranges, and rounding keeps it so.
        var first = left.Min * right.Min;
        var second = left.Min * right.Max;
        var third = left.Max * right.Min;
        var fourth = left.Max * right.Max;
        var min = MathF.Min(MathF.Min(first, second), MathF.Min(third, fourth));
        var max = MathF.Max(MathF.Max(first, second), MathF.Max(third, fourth));
        var bounds = new ValueBounds(min, max, false, false);

        // A zero product, exact or rounded down from a tiny one, takes the sign the operands' signs give.
        var mixedSigns = (left.CanBeNegativeSigned && right.CanBePositiveSigned)
            || (left.CanBePositiveSigned && right.CanBeNegativeSigned);

        return bounds with { NegativeZero = bounds.ContainsZero && mixedSigns };
    }

    /// <summary>The negation of a value.</summary>
    public static ValueBounds Negate(ValueBounds value) => new(
        -value.Max,
        -value.Min,
        value.ContainsZero,
        value.NaN);

    /// <summary>
    /// <c>min</c> or <c>max</c> of two values, at a point (Java's Math.min and Math.max) or over a volume. The result
    /// is one of the two.
    /// </summary>
    public static ValueBounds Choose(BinaryOperation operation, ValueBounds left, ValueBounds right)
    {
        var smaller = operation == BinaryOperation.Min;
        var min = smaller ? MathF.Min(left.Min, right.Min) : MathF.Max(left.Min, right.Min);
        var max = smaller ? MathF.Min(left.Max, right.Max) : MathF.Max(left.Max, right.Max);

        return new ValueBounds(min, max, left.NegativeZero || right.NegativeZero, left.NaN || right.NaN);
    }

    /// <summary>
    /// <see cref="FloatMath.Clamp"/> of a value. Infinities are clamped too, but NaN passes through and so does -0
    /// inside the range; a bound of -0 can also be returned.
    /// </summary>
    public static ValueBounds Clamp(ValueBounds value, float min, float max)
    {
        var lower = FloatMath.Clamp(value.Min, min, max);
        var upper = FloatMath.Clamp(value.Max, min, max);
        var passesNegativeZero = value.NegativeZero && min <= 0.0f && max >= 0.0f;
        var negativeZero = passesNegativeZero || IsNegativeZero(min) || IsNegativeZero(max);

        return new ValueBounds(lower, upper, negativeZero, value.NaN);
    }

    /// <summary>A unary operation of a value, or <see cref="Unknown"/> for those without a rule.</summary>
    public static ValueBounds Apply(UnaryOperation operation, ValueBounds value)
    {
        switch (operation)
        {
            case UnaryOperation.Abs:
            {
                var magnitude = Abs(value);
                return magnitude with { NegativeZero = false };
            }
            case UnaryOperation.Square:
            {
                // Both factors have the same sign, so the product is never -0.
                var magnitude = Abs(value);
                var square = new ValueBounds(magnitude.Min * magnitude.Min, magnitude.Max * magnitude.Max, false,
                    value.NaN);

                return square;
            }
            case UnaryOperation.Cube:
            {
                // (x * x) * x rises with x, also after rounding, and keeps x's sign.
                var cube = new ValueBounds(
                    value.Min * value.Min * value.Min,
                    value.Max * value.Max * value.Max,
                    false,
                    value.NaN);

                return cube with { NegativeZero = cube.ContainsZero && value.CanBeNegativeSigned };
            }
            case UnaryOperation.Negate:
                return Negate(value);
            case UnaryOperation.HalfNegative:
                return ScaleNegatives(value, 0.5f);
            case UnaryOperation.QuarterNegative:
                return ScaleNegatives(value, 0.25f);
            case UnaryOperation.Squeeze:
            {
                // The input is clamped to [-1, 1] first; x / 2 - x³ / 24 then stays within ±0.5, with x's sign.
                var squeezed = new ValueBounds(-0.5f, 0.5f, false, value.NaN);
                return squeezed with { NegativeZero = value.CanBeNegativeSigned };
            }
            default:
                // Square roots, reciprocals, logarithms and signs have no rule; nothing relies on them yet.
                return Unknown;
        }
    }

    /// <summary>
    /// A noise stack sampled at coordinates up to <paramref name="maxCoordinate"/> times each layer's frequency, as
    /// both its point and volume paths sample it, or <see cref="Unknown"/>.
    /// </summary>
    /// <param name="maxCoordinate">The largest magnitude of a coordinate before the layers' frequencies, or
    /// infinity or NaN when it isn't bounded.</param>
    public static ValueBounds Noise(NoiseStack noise, double maxCoordinate)
    {
        var total = 0.0;

        foreach (var layer in noise.Layers)
        {
            // Smeared noise quantises y inside a cell, which this bound doesn't account for.
            if (layer.Noise.GetType() != typeof(PerlinNoise) || !float.IsFinite(layer.Amplitude))
                return Unknown;

            if (!(maxCoordinate * Math.Abs(layer.Frequency) <= MaxNoiseCoordinate))
                return Unknown;

            total += Math.Abs(layer.Amplitude) * OctaveBound;
        }

        // The float sum adds an ulp or so per layer, far below the octave slack.
        var bound = RoundUp(total * 1.0001);
        if (!float.IsFinite(bound))
            return Unknown;

        return new ValueBounds(-bound, bound, bound > 0.0f, false);
    }

    /// <summary>
    /// The largest coordinate magnitude a noise is sampled at: an int block coordinate times a scale, plus a shift of
    /// the given bounds. NaN when it isn't bounded.
    /// </summary>
    public static double NoiseCoordinate(double scale, ValueBounds shift)
    {
        if (!shift.IsFinite || !double.IsFinite(scale))
            return double.NaN;

        var shiftMagnitude = Math.Max(Math.Abs((double)shift.Min), Math.Abs((double)shift.Max));

        // Twice the product, for the volume path's scale times frequency rounding before the coordinate.
        return 2.0 * ((double)int.MaxValue * Math.Abs(scale) + shiftMagnitude);
    }

    /// <summary>
    /// A spline segment between two points, as <see cref="MultipointSpline"/> evaluates it: the straight line between
    /// the values plus a cubic correction from the derivatives, for t from 0 to 1.
    /// </summary>
    public static ValueBounds SplineSegment(
        float location1,
        float location2,
        ValueBounds value1,
        ValueBounds value2,
        float derivative1,
        float derivative2)
    {
        if (!value1.IsFinite || !value2.IsFinite)
            return Unknown;

        // The line is a lerp of the values. The correction is t(1 - t) ≤ 1/4 times a lerp of
        // a = d1 (x2 - x1) - (y2 - y1) and b = -d2 (x2 - x1) + (y2 - y1), so it's at most a quarter of the larger.
        var width = Math.Abs((double)location2 - location1);
        var values = Math.Max(Magnitude(value1), Magnitude(value2));
        var first = Math.Abs((double)derivative1) * width + 2.0 * values;
        var second = Math.Abs((double)derivative2) * width + 2.0 * values;
        var correction = 0.25 * Math.Max(first, second);

        // Float rounding of the lerps and products stays far below this margin.
        var margin = 1e-5 * (values + correction);
        var lower = Math.Min(value1.Min, value2.Min) - correction - margin;
        var upper = Math.Max(value1.Max, value2.Max) + correction + margin;

        return FiniteRange(lower, upper);
    }

    /// <summary>
    /// A spline's straight extension beyond its first or last point: the point's value plus its derivative times the
    /// coordinate's distance from the point's location, for coordinates from <paramref name="from"/> to
    /// <paramref name="to"/>.
    /// </summary>
    public static ValueBounds SplineExtension(ValueBounds value, float derivative, float location, float from, float to)
    {
        if (derivative == 0.0f)
            return value;

        if (!value.IsFinite)
            return Unknown;

        // value + derivative * (input - location) is monotonic in the value and the input, also after rounding.
        var first = value.Min + derivative * (from - location);
        var second = value.Min + derivative * (to - location);
        var third = value.Max + derivative * (from - location);
        var fourth = value.Max + derivative * (to - location);
        var min = MathF.Min(MathF.Min(first, second), MathF.Min(third, fourth));
        var max = MathF.Max(MathF.Max(first, second), MathF.Max(third, fourth));

        return new ValueBounds(min, max, min <= 0.0f && max >= 0.0f, false);
    }

    private static ValueBounds Abs(ValueBounds value)
    {
        if (value.Min >= 0.0f)
            return value;

        if (value.Max <= 0.0f)
            return new ValueBounds(-value.Max, -value.Min, false, value.NaN);

        return new ValueBounds(0.0f, MathF.Max(-value.Min, value.Max), false, value.NaN);
    }

    // half_negative and quarter_negative: positive values stay, others are scaled, which rises with the value.
    private static ValueBounds ScaleNegatives(ValueBounds value, float scale)
    {
        var min = value.Min > 0.0f ? value.Min : value.Min * scale;
        var max = value.Max > 0.0f ? value.Max : value.Max * scale;
        var scaled = new ValueBounds(min, max, false, value.NaN);

        return scaled with { NegativeZero = scaled.ContainsZero && value.CanBeNegativeSigned };
    }

    private static double Magnitude(ValueBounds value) =>
        Math.Max(Math.Abs((double)value.Min), Math.Abs((double)value.Max));

    // A range computed in double, widened outwards to floats, or unknown when it doesn't fit comfortably in float:
    // intermediate float values may be a few times larger than the range's ends.
    private static ValueBounds FiniteRange(double lower, double upper)
    {
        const double limit = float.MaxValue / 64.0;
        if (!(lower >= -limit && upper <= limit))
            return Unknown;

        var min = RoundDown(lower);
        var max = RoundUp(upper);

        return new ValueBounds(min, max, min <= 0.0f && max >= 0.0f, false);
    }

    private static float RoundUp(double value)
    {
        var rounded = (float)value;
        return rounded < value ? MathF.BitIncrement(rounded) : rounded;
    }

    private static float RoundDown(double value)
    {
        var rounded = (float)value;
        return rounded > value ? MathF.BitDecrement(rounded) : rounded;
    }

    private static bool IsNegativeZero(float value) => value == 0.0f && float.IsNegative(value);
}
