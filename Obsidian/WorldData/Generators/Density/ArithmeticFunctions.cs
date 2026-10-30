namespace Obsidian.WorldData.Generators.Density;

/// <summary>The one-input arithmetic types (<c>abs</c>, <c>square</c>, ... with an <c>input</c>).</summary>
internal enum UnaryOperation
{
    Abs,
    Square,
    Cube,
    Sqrt,
    HalfNegative,
    QuarterNegative,
    Reciprocal,
    Negate,
    Squeeze,
    Log,
    Sign
}

/// <summary>A one-input arithmetic function, in float like vanilla's UnaryFunction samplers.</summary>
internal sealed class UnaryFunction(UnaryOperation operation, DensityFunction input) : DensityFunction
{
    public override float Sample(int x, int y, int z) => this.Apply(input.Sample(x, y, z));

    public override void SampleVolume(Span<float> output, in DensityVolume volume)
    {
        input.SampleVolume(output, volume);

        for (var i = 0; i < output.Length; i++)
            output[i] = this.Apply(output[i]);
    }

    private float Apply(float value) => operation switch
    {
        UnaryOperation.Abs => MathF.Abs(value),
        UnaryOperation.Square => value * value,
        UnaryOperation.Cube => value * value * value,
        UnaryOperation.Sqrt => MathF.Sqrt(value),
        UnaryOperation.HalfNegative => value > 0.0f ? value : value * 0.5f,
        UnaryOperation.QuarterNegative => value > 0.0f ? value : value * 0.25f,
        UnaryOperation.Reciprocal => 1.0f / value,
        UnaryOperation.Negate => -value,
        UnaryOperation.Squeeze => Squeeze(value),
        UnaryOperation.Log => (float)Math.Log(value),
        _ => Sign(value)
    };

    private static float Squeeze(float value)
    {
        var clamped = FloatMath.Clamp(value, -1.0f, 1.0f);
        return clamped / 2.0f - clamped * clamped * clamped / 24.0f;
    }

    // Java's Math.signum: NaN and signed zeros pass through.
    private static float Sign(float value)
    {
        if (value > 0.0f)
            return 1.0f;

        if (value < 0.0f)
            return -1.0f;

        return value;
    }
}

/// <summary>The rounding types (<c>floor</c>, <c>round</c>, <c>ceil</c>, <c>truncate</c>).</summary>
internal enum RoundingMode
{
    Floor,
    Round,
    Ceil,
    Truncate
}

/// <summary>
/// A rounding function: <c>input</c> rounded to a multiple of <c>multiple</c> (1 by default; 0 leaves it as is).
/// </summary>
internal sealed class RoundFunction(RoundingMode mode, DensityFunction input, DensityFunction multiple)
    : DensityFunction
{
    public override float Sample(int x, int y, int z)
    {
        var value = input.Sample(x, y, z);
        var step = multiple.Sample(x, y, z);

        return this.Apply(value, step);
    }

    public override void SampleVolume(Span<float> output, in DensityVolume volume)
    {
        input.SampleVolume(output, volume);
        using var steps = DensityBuffer.Sample(multiple, volume);

        for (var i = 0; i < output.Length; i++)
            output[i] = this.Apply(output[i], steps.Span[i]);
    }

    // Vanilla skips the division for a constant multiple of 1, which gives the same result.
    private float Apply(float value, float step) =>
        step == 0.0f ? value : this.ToInteger(value / step) * step;

    private float ToInteger(float value) => mode switch
    {
        RoundingMode.Floor => MathF.Floor(value),
        RoundingMode.Round => FloatMath.Round(value),
        RoundingMode.Ceil => MathF.Ceiling(value),
        _ => value > 0.0f ? MathF.Floor(value) : MathF.Ceiling(value)
    };
}

/// <summary>The two-input arithmetic types, with operands <c>left</c> and <c>right</c>.</summary>
internal enum BinaryOperation
{
    Add,
    Sub,
    Mul,
    Div,
    Min,
    Max
}

/// <summary>
/// A two-input arithmetic function. Build it with <see cref="Create"/>, which specialises on constant operands like
/// vanilla's compiler.
/// </summary>
/// <remarks>
/// Matches vanilla's BinaryFunction samplers. At a point, mul and div return 0 for a zero left side without sampling
/// the right, and min and max use Java's Math.min and Math.max; over a volume both sides are always sampled and min
/// and max replace a value only when the other is strictly smaller or larger. The two differ only for NaN and signed
/// zeros, but those are vanilla's results too.
/// </remarks>
internal abstract class BinaryFunction : DensityFunction
{
    public static DensityFunction Create(BinaryOperation operation, DensityFunction left, DensityFunction right)
    {
        var leftConstant = left as ConstantFunction;
        var rightConstant = right as ConstantFunction;

        if (leftConstant is not null)
            return CreateWithConstantLeft(operation, leftConstant.Value, right);

        if (rightConstant is not null)
            return CreateWithConstantRight(operation, left, rightConstant.Value);

        return new Both(operation, left, right);
    }

    private static DensityFunction CreateWithConstantLeft(BinaryOperation operation, float left, DensityFunction right)
    {
        return operation switch
        {
            BinaryOperation.Add => new WithConstant(BinaryOperation.Add, right, left),
            BinaryOperation.Mul => new WithConstant(BinaryOperation.Mul, right, left),
            BinaryOperation.Min => new WithConstant(BinaryOperation.Min, right, left),
            BinaryOperation.Max => new WithConstant(BinaryOperation.Max, right, left),
            _ => new ConstantLeft(operation, left, right)
        };
    }

    private static WithConstant CreateWithConstantRight(BinaryOperation operation, DensityFunction left, float right)
    {
        return operation switch
        {
            // Vanilla adds the negated constant and multiplies by the reciprocal; the reciprocal rounds.
            BinaryOperation.Sub => new WithConstant(BinaryOperation.Add, left, -right),
            BinaryOperation.Div => new WithConstant(BinaryOperation.Mul, left, 1.0f / right),
            _ => new WithConstant(operation, left, right)
        };
    }

    /// <summary>Both operands are functions.</summary>
    private sealed class Both(BinaryOperation operation, DensityFunction left, DensityFunction right) : BinaryFunction
    {
        public override float Sample(int x, int y, int z)
        {
            var leftValue = left.Sample(x, y, z);

            switch (operation)
            {
                case BinaryOperation.Add:
                    return leftValue + right.Sample(x, y, z);
                case BinaryOperation.Sub:
                    return leftValue - right.Sample(x, y, z);
                case BinaryOperation.Mul:
                    return leftValue == 0.0f ? 0.0f : leftValue * right.Sample(x, y, z);
                case BinaryOperation.Div:
                    return leftValue == 0.0f ? 0.0f : leftValue / right.Sample(x, y, z);
                case BinaryOperation.Min:
                    return MathF.Min(leftValue, right.Sample(x, y, z));
                default:
                    return MathF.Max(leftValue, right.Sample(x, y, z));
            }
        }

        public override void SampleVolume(Span<float> output, in DensityVolume volume)
        {
            left.SampleVolume(output, volume);
            using var rightValues = DensityBuffer.Sample(right, volume);
            var rights = rightValues.Span;

            for (var i = 0; i < output.Length; i++)
                output[i] = Combine(operation, output[i], rights[i]);
        }
    }

    /// <summary>A function and a constant, for the commutative operations and the rewritten sub and div.</summary>
    private sealed class WithConstant(BinaryOperation operation, DensityFunction input, float constant)
        : BinaryFunction
    {
        public override float Sample(int x, int y, int z)
        {
            var value = input.Sample(x, y, z);

            return operation switch
            {
                BinaryOperation.Add => value + constant,
                BinaryOperation.Mul => value * constant,
                BinaryOperation.Min => MathF.Min(value, constant),
                _ => MathF.Max(value, constant)
            };
        }

        public override void SampleVolume(Span<float> output, in DensityVolume volume)
        {
            input.SampleVolume(output, volume);

            for (var i = 0; i < output.Length; i++)
                output[i] = Combine(operation, output[i], constant);
        }
    }

    /// <summary>A constant minus or divided by a function.</summary>
    private sealed class ConstantLeft(BinaryOperation operation, float constant, DensityFunction input)
        : BinaryFunction
    {
        public override float Sample(int x, int y, int z) => this.Apply(input.Sample(x, y, z));

        public override void SampleVolume(Span<float> output, in DensityVolume volume)
        {
            input.SampleVolume(output, volume);

            for (var i = 0; i < output.Length; i++)
                output[i] = this.Apply(output[i]);
        }

        private float Apply(float value) => operation == BinaryOperation.Sub ? constant - value : constant / value;
    }

    // Vanilla's volume loops: sub adds the negation, and min and max replace only on a strict comparison.
    private static float Combine(BinaryOperation operation, float left, float right) => operation switch
    {
        BinaryOperation.Add => left + right,
        BinaryOperation.Sub => left + -right,
        BinaryOperation.Mul => left * right,
        BinaryOperation.Div => left / right,
        BinaryOperation.Min => right < left ? right : left,
        _ => right > left ? right : left
    };
}

/// <summary>
/// <c>pow</c>: <c>base</c> to the power of <c>exponent</c>, computed in double with Java's Math.pow rules and
/// narrowed. Build it with <see cref="Create"/>, which rewrites some constant exponents like vanilla's compiler.
/// </summary>
/// <remarks>
/// Over a volume both operands are sampled as volumes first, like vanilla's PowFunction samplers. Java's Math.pow may
/// still differ from .NET's by an ulp before narrowing, which the captures haven't shown.
/// </remarks>
internal sealed class PowFunction(DensityFunction baseFunction, DensityFunction exponent) : DensityFunction
{
    public static DensityFunction Create(DensityFunction baseFunction, DensityFunction exponent)
    {
        var constantBase = baseFunction is ConstantFunction;
        var constantExponent = exponent as ConstantFunction;

        if (constantBase || constantExponent is null)
            return new PowFunction(baseFunction, exponent);

        return CreateWithConstantExponent(baseFunction, constantExponent.Value);
    }

    public override float Sample(int x, int y, int z)
    {
        var value = baseFunction.Sample(x, y, z);
        var power = exponent.Sample(x, y, z);

        return Pow(value, power);
    }

    public override void SampleVolume(Span<float> output, in DensityVolume volume)
    {
        baseFunction.SampleVolume(output, volume);
        using var powers = DensityBuffer.Sample(exponent, volume);

        for (var i = 0; i < output.Length; i++)
            output[i] = Pow(output[i], powers.Span[i]);
    }

    // For an exponent of ±0.5, ±1, ±2 or ±3 vanilla's compiler takes the float root, the base, its square or its cube,
    // and the reciprocal of that for a negative exponent. They round differently from Math.pow.
    private static DensityFunction CreateWithConstantExponent(DensityFunction baseFunction, float exponent)
    {
        var magnitude = MathF.Abs(exponent);
        DensityFunction power;

        if (magnitude == 0.5f)
            power = new UnaryFunction(UnaryOperation.Sqrt, baseFunction);
        else if (magnitude == 1.0f)
            power = baseFunction;
        else if (magnitude == 2.0f)
            power = new UnaryFunction(UnaryOperation.Square, baseFunction);
        else if (magnitude == 3.0f)
            power = new UnaryFunction(UnaryOperation.Cube, baseFunction);
        else
            return new PowFunction(baseFunction, new ConstantFunction(exponent));

        return exponent < 0.0f ? new UnaryFunction(UnaryOperation.Reciprocal, power) : power;
    }

    // Java's special cases differ from .NET's: 1 to a NaN power and ±1 to an infinite power are NaN, not 1.
    private static float Pow(float value, float exponent) => (float)JavaMath.Pow(value, exponent);
}
