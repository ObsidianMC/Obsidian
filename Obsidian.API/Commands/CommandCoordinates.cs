using System.Globalization;

namespace Obsidian.API.Commands;

/// <summary>
/// Reads vanilla's command coordinates: absolute numbers, <c>~</c> offsets from where the command runs, or <c>^</c>
/// offsets along the way it faces (left, up, forwards).
/// </summary>
public static class CommandCoordinates
{
    /// <summary>
    /// Reads three coordinates separated by spaces, relative to <paramref name="context"/>'s
    /// <see cref="CommandContext.Origin"/> and <see cref="CommandContext.Rotation"/>.
    /// </summary>
    /// <param name="centerBlocks">
    /// Whether absolute whole numbers of x and z name the block's center, as vanilla's <c>vec3</c> arguments do.
    /// </param>
    /// <param name="wholeNumbers">Whether absolute numbers must be whole, as in a block position.</param>
    public static bool TryParse(string input, CommandContext context, bool centerBlocks, bool wholeNumbers, out VectorD result)
    {
        result = default;

        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return false;

        var local = parts.Count(part => part.StartsWith('^'));
        if (local is not (0 or 3))
            return false;

        var origin = context.Origin;

        if (local == 3)
        {
            if (origin is not VectorD position || context.Rotation is not var (yaw, pitch))
                return false;

            Span<double> offsets = stackalloc double[3];
            for (var i = 0; i < 3; i++)
            {
                if (!TryParseOffset(parts[i].AsSpan(1), out offsets[i]))
                    return false;
            }

            result = Local(position, yaw.Degrees, pitch.Degrees, offsets[0], offsets[1], offsets[2]);
            return true;
        }

        Span<double> values = stackalloc double[3];
        for (var i = 0; i < 3; i++)
        {
            var part = parts[i];

            if (part.StartsWith('~'))
            {
                if (origin is not VectorD position || !TryParseOffset(part.AsSpan(1), out var offset))
                    return false;

                values[i] = (i switch { 0 => position.X, 1 => position.Y, _ => position.Z }) + offset;
                continue;
            }

            if (wholeNumbers)
            {
                if (!int.TryParse(part, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole))
                    return false;

                values[i] = whole;
                continue;
            }

            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
                return false;

            // Vanilla centers whole x and z coordinates on their block.
            values[i] = centerBlocks && i != 1 && !part.Contains('.') ? number + 0.5 : number;
        }

        result = new VectorD(values[0], values[1], values[2]);
        return true;
    }

    /// <summary>
    /// The position <paramref name="left"/>, <paramref name="up"/> and <paramref name="forwards"/> blocks along the
    /// directions of a rotation from <paramref name="origin"/>, as vanilla's <c>LocalCoordinates</c> places it.
    /// </summary>
    public static VectorD Local(VectorD origin, float yaw, float pitch, double left, double up, double forwards)
    {
        const double ToRadians = Math.PI / 180;

        var f = Math.Cos((yaw + 90) * ToRadians);
        var g = Math.Sin((yaw + 90) * ToRadians);
        var h = Math.Cos(-pitch * ToRadians);
        var i = Math.Sin(-pitch * ToRadians);
        var j = Math.Cos((-pitch + 90) * ToRadians);
        var k = Math.Sin((-pitch + 90) * ToRadians);

        var forward = new VectorD(f * h, i, g * h);
        var upward = new VectorD(f * j, k, g * j);

        // Left is the negated cross product of forwards and up.
        var leftward = new VectorD(
            -(forward.Y * upward.Z - forward.Z * upward.Y),
            -(forward.Z * upward.X - forward.X * upward.Z),
            -(forward.X * upward.Y - forward.Y * upward.X));

        return new VectorD(
            origin.X + forward.X * forwards + upward.X * up + leftward.X * left,
            origin.Y + forward.Y * forwards + upward.Y * up + leftward.Y * left,
            origin.Z + forward.Z * forwards + upward.Z * up + leftward.Z * left);
    }

    // An offset after ~ or ^, which is 0 when left out.
    private static bool TryParseOffset(ReadOnlySpan<char> text, out double offset)
    {
        if (text.IsEmpty)
        {
            offset = 0;
            return true;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out offset) && double.IsFinite(offset);
    }
}
