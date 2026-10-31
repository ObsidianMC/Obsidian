using System.Numerics;
using System.Runtime.InteropServices;
using Vector = System.Numerics.Vector;

namespace Obsidian.WorldData.Generators.Density;

/// <summary>
/// The elementwise passes of density functions over a buffer, in float like vanilla's volume loops.
/// </summary>
/// <remarks>
/// Each operation picks its loop once rather than per value. Loops use vector instructions only where they round
/// exactly like the scalar operation: IEEE addition, multiplication, division and square root, and comparisons that
/// select one operand, which keep vanilla's NaN and signed-zero results. Nothing contracts a product and a sum into a
/// fused multiply-add.
/// </remarks>
internal static class DensityLoops
{
    /// <summary>Replaces each left value with vanilla's volume result of the operation with the right value.</summary>
    public static void Combine(BinaryOperation operation, Span<float> left, ReadOnlySpan<float> right)
    {
        var lefts = MemoryMarshal.Cast<float, Vector<float>>(left);
        var rights = MemoryMarshal.Cast<float, Vector<float>>(right[..left.Length]);
        var tail = lefts.Length * Vector<float>.Count;

        switch (operation)
        {
            case BinaryOperation.Add:
                for (var i = 0; i < lefts.Length; i++)
                    lefts[i] += rights[i];

                for (var i = tail; i < left.Length; i++)
                    left[i] += right[i];

                break;
            case BinaryOperation.Sub:
                // Vanilla adds the negation.
                for (var i = 0; i < lefts.Length; i++)
                    lefts[i] += -rights[i];

                for (var i = tail; i < left.Length; i++)
                    left[i] += -right[i];

                break;
            case BinaryOperation.Mul:
                for (var i = 0; i < lefts.Length; i++)
                    lefts[i] *= rights[i];

                for (var i = tail; i < left.Length; i++)
                    left[i] *= right[i];

                break;
            case BinaryOperation.Div:
                for (var i = 0; i < lefts.Length; i++)
                    lefts[i] /= rights[i];

                for (var i = tail; i < left.Length; i++)
                    left[i] /= right[i];

                break;
            case BinaryOperation.Min:
                // The right value replaces the left only when strictly smaller.
                for (var i = 0; i < lefts.Length; i++)
                    lefts[i] = Vector.ConditionalSelect(Vector.LessThan(rights[i], lefts[i]), rights[i], lefts[i]);

                for (var i = tail; i < left.Length; i++)
                    left[i] = right[i] < left[i] ? right[i] : left[i];

                break;
            default:
                for (var i = 0; i < lefts.Length; i++)
                {
                    var larger = Vector.GreaterThan(rights[i], lefts[i]);
                    lefts[i] = Vector.ConditionalSelect(larger, rights[i], lefts[i]);
                }

                for (var i = tail; i < left.Length; i++)
                    left[i] = right[i] > left[i] ? right[i] : left[i];

                break;
        }
    }

    /// <summary>
    /// Replaces each value with vanilla's volume result of the operation with a constant: add, mul, min or max.
    /// </summary>
    public static void Combine(BinaryOperation operation, Span<float> values, float constant)
    {
        var vectors = MemoryMarshal.Cast<float, Vector<float>>(values);
        var tail = vectors.Length * Vector<float>.Count;
        var constants = new Vector<float>(constant);

        switch (operation)
        {
            case BinaryOperation.Add:
                for (var i = 0; i < vectors.Length; i++)
                    vectors[i] += constants;

                for (var i = tail; i < values.Length; i++)
                    values[i] += constant;

                break;
            case BinaryOperation.Mul:
                for (var i = 0; i < vectors.Length; i++)
                    vectors[i] *= constants;

                for (var i = tail; i < values.Length; i++)
                    values[i] *= constant;

                break;
            case BinaryOperation.Min:
                for (var i = 0; i < vectors.Length; i++)
                    vectors[i] = Vector.ConditionalSelect(Vector.LessThan(constants, vectors[i]), constants, vectors[i]);

                for (var i = tail; i < values.Length; i++)
                    values[i] = constant < values[i] ? constant : values[i];

                break;
            case BinaryOperation.Max:
                for (var i = 0; i < vectors.Length; i++)
                {
                    var larger = Vector.GreaterThan(constants, vectors[i]);
                    vectors[i] = Vector.ConditionalSelect(larger, constants, vectors[i]);
                }

                for (var i = tail; i < values.Length; i++)
                    values[i] = constant > values[i] ? constant : values[i];

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, "has no constant form");
        }
    }

    /// <summary><see cref="FloatMath.Clamp"/> of each value: NaN stays NaN.</summary>
    public static void Clamp(Span<float> values, float min, float max)
    {
        // Math.min picks a negative zero bound over a positive zero value, which a comparison can't tell apart.
        var exactBound = max != 0.0f || !float.IsNegative(max);
        var vectors = exactBound ? MemoryMarshal.Cast<float, Vector<float>>(values) : [];
        var tail = vectors.Length * Vector<float>.Count;
        var mins = new Vector<float>(min);
        var maxes = new Vector<float>(max);

        for (var i = 0; i < vectors.Length; i++)
            vectors[i] = Clamp(vectors[i], mins, maxes);

        for (var i = tail; i < values.Length; i++)
            values[i] = FloatMath.Clamp(values[i], min, max);
    }

    /// <summary>Applies a unary operation to each value, like vanilla's UnaryFunction volume loop.</summary>
    public static void Apply(UnaryOperation operation, Span<float> values)
    {
        var vectors = MemoryMarshal.Cast<float, Vector<float>>(values);
        var tail = vectors.Length * Vector<float>.Count;

        switch (operation)
        {
            case UnaryOperation.Abs:
                for (var i = 0; i < vectors.Length; i++)
                    vectors[i] = Vector.Abs(vectors[i]);

                break;
            case UnaryOperation.Square:
                for (var i = 0; i < vectors.Length; i++)
                    vectors[i] *= vectors[i];

                break;
            case UnaryOperation.Cube:
                for (var i = 0; i < vectors.Length; i++)
                    vectors[i] = vectors[i] * vectors[i] * vectors[i];

                break;
            case UnaryOperation.Negate:
                for (var i = 0; i < vectors.Length; i++)
                    vectors[i] = -vectors[i];

                break;
            case UnaryOperation.HalfNegative:
                ScaleNegatives(vectors, 0.5f);
                break;
            case UnaryOperation.QuarterNegative:
                ScaleNegatives(vectors, 0.25f);
                break;
            case UnaryOperation.Squeeze:
                Squeeze(vectors);
                break;
            default:
                // Square roots, reciprocals, logarithms and signs are rare; they take the scalar loop.
                tail = 0;
                break;
        }

        for (var i = tail; i < values.Length; i++)
            values[i] = UnaryFunction.Apply(operation, values[i]);
    }

    /// <summary>Vanilla's lerp of each start and end by the alpha in <paramref name="output"/>.</summary>
    public static void Lerp(Span<float> output, ReadOnlySpan<float> starts, ReadOnlySpan<float> ends)
    {
        for (var i = 0; i < output.Length; i++)
            output[i] = FloatMath.Lerp(output[i], starts[i], ends[i]);
    }

    private static Vector<float> Clamp(Vector<float> value, Vector<float> min, Vector<float> max)
    {
        // value < min ? min : Math.min(value, max), where Math.min keeps NaN.
        var upper = Vector.ConditionalSelect(Vector.GreaterThan(value, max), max, value);

        return Vector.ConditionalSelect(Vector.LessThan(value, min), min, upper);
    }

    private static void ScaleNegatives(Span<Vector<float>> vectors, float scale)
    {
        var scales = new Vector<float>(scale);

        for (var i = 0; i < vectors.Length; i++)
        {
            var positive = Vector.GreaterThan(vectors[i], Vector<float>.Zero);
            vectors[i] = Vector.ConditionalSelect(positive, vectors[i], vectors[i] * scales);
        }
    }

    // Vanilla's squeeze: the value clamped to [-1, 1], halved, minus its cube over 24.
    private static void Squeeze(Span<Vector<float>> vectors)
    {
        var ones = Vector<float>.One;
        var twos = new Vector<float>(2.0f);
        var twentyFours = new Vector<float>(24.0f);

        for (var i = 0; i < vectors.Length; i++)
        {
            var clamped = Clamp(vectors[i], -ones, ones);
            vectors[i] = clamped / twos - clamped * clamped * clamped / twentyFours;
        }
    }
}
