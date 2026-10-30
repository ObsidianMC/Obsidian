namespace Obsidian.API.Utilities;

/// <summary>Java math semantics needed when evaluating vanilla data.</summary>
public static class JavaMath
{
    /// <summary>Java Math.pow, including its NaN, infinity and signed-zero rules.</summary>
    public static double Pow(double value, double exponent)
    {
        // Java tests a zero exponent before either NaN operand: even NaN raised to zero is one.
        if (exponent == 0)
        {
            return 1;
        }

        if (double.IsNaN(exponent) || double.IsNaN(value))
        {
            return double.NaN;
        }

        var magnitude = Math.Abs(value);
        if (double.IsInfinity(exponent))
        {
            // Unlike .NET, Java leaves both +1 and -1 raised to either infinity undefined.
            if (magnitude == 1)
            {
                return double.NaN;
            }

            return (magnitude > 1) == (exponent > 0) ? double.PositiveInfinity : 0;
        }

        var integralExponent = exponent == Math.Truncate(exponent);
        if (value == 0 || double.IsInfinity(value))
        {
            var result = (value == 0) == (exponent > 0) ? 0 : double.PositiveInfinity;
            var oddExponent = integralExponent && Math.Abs(exponent) % 2 == 1;
            return oddExponent ? Math.CopySign(result, value) : result;
        }

        if (value < 0 && !integralExponent)
        {
            return double.NaN;
        }

        return Math.Pow(value, exponent);
    }
}
