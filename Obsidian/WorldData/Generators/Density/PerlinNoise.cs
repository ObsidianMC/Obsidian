using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Generators.Density;

/// <summary>
/// The lattice shared by 26.3's gradient noises: a random offset and a shuffled permutation of 0 to 255.
/// </summary>
/// <remarks>
/// Matches vanilla's GradientNoise. The random draws (three offsets, then the permutation shuffle) are in vanilla's
/// order, since they decide every value; a zero offset scale still draws the offsets.
/// </remarks>
internal abstract class GradientNoise
{
    // Vanilla's 16 gradients: the 12 cube edge directions, then four of them again.
    protected static readonly (int X, int Y, int Z)[] gradients =
    [
        (1, 1, 0), (-1, 1, 0), (1, -1, 0), (-1, -1, 0),
        (1, 0, 1), (-1, 0, 1), (1, 0, -1), (-1, 0, -1),
        (0, 1, 1), (0, -1, 1), (0, 1, -1), (0, -1, -1),
        (1, 1, 0), (0, -1, 1), (-1, 1, 0), (0, -1, -1)
    ];

    private readonly byte[] permutations = new byte[256];

    protected GradientNoise(IRandomSource random, double offsetScale)
    {
        this.OffsetX = random.NextDouble() * offsetScale;
        this.OffsetY = random.NextDouble() * offsetScale;
        this.OffsetZ = random.NextDouble() * offsetScale;

        for (var i = 0; i < 256; i++)
            this.permutations[i] = (byte)i;

        for (var i = 0; i < 256; i++)
        {
            var other = random.NextInt(256 - i) + i;
            (this.permutations[i], this.permutations[other]) = (this.permutations[other], this.permutations[i]);
        }
    }

    protected double OffsetX { get; }

    protected double OffsetY { get; }

    protected double OffsetZ { get; }

    protected int Permute(int value) => this.permutations[value & 0xFF];
}

/// <summary>
/// One octave of 26.3's gradient noise, sampled in single precision.
/// </summary>
/// <remarks>
/// Matches vanilla's PerlinNoise bit for bit: the coordinates are wrapped and offset in double, the position inside the
/// lattice cell is narrowed to float, and the gradients, fade curve and interpolation are float.
/// </remarks>
internal class PerlinNoise(IRandomSource random) : GradientNoise(random, 256.0)
{
    // Coordinates are wrapped into vanilla's range of 2^25 so that large ones keep their precision (no far lands).
    private const double WrapPeriod = 33554432.0;
    private static readonly double halfWrapPeriod = Math.BitDecrement(16777216.0);

    /// <summary>
    /// The noise at a point. <paramref name="y"/> is passed unwrapped to <see cref="SampleCell"/>, which the smeared
    /// variant needs.
    /// </summary>
    public float Get(double x, double y, double z)
    {
        var wrappedX = Wrap(x) + this.OffsetX;
        var wrappedY = Wrap(y) + this.OffsetY;
        var wrappedZ = Wrap(z) + this.OffsetZ;

        var floorX = (int)Math.Floor(wrappedX);
        var floorY = (int)Math.Floor(wrappedY);
        var floorZ = (int)Math.Floor(wrappedZ);

        var relativeX = (float)(wrappedX - floorX);
        var relativeZ = (float)(wrappedZ - floorZ);

        return this.SampleCell(floorX, floorY, floorZ, relativeX, wrappedY - floorY, relativeZ, y);
    }

    /// <summary>
    /// Samples the lattice cell at a position inside it. <paramref name="relativeY"/> stays double so the smeared
    /// variant can adjust it before narrowing.
    /// </summary>
    /// <param name="originalY">The y coordinate before wrapping and offsetting.</param>
    protected virtual float SampleCell(int floorX, int floorY, int floorZ, float relativeX, double relativeY,
        float relativeZ, double originalY)
    {
        var y = (float)relativeY;
        return this.SampleAndLerp(floorX, floorY, floorZ, relativeX, y, relativeZ, y);
    }

    /// <summary>
    /// Vanilla's trilinear blend of the corner gradients. <paramref name="relativeY"/> places the corners and
    /// <paramref name="fadeY"/> drives the y fade; they differ only for smeared noise.
    /// </summary>
    protected float SampleAndLerp(int x, int y, int z, float relativeX, float relativeY, float relativeZ, float fadeY)
    {
        var x0 = this.Permute(x);
        var x1 = this.Permute(x + 1);
        var xy00 = this.Permute(x0 + y);
        var xy01 = this.Permute(x0 + y + 1);
        var xy10 = this.Permute(x1 + y);
        var xy11 = this.Permute(x1 + y + 1);

        var d000 = Dot(this.Permute(xy00 + z), relativeX, relativeY, relativeZ);
        var d100 = Dot(this.Permute(xy10 + z), relativeX - 1.0f, relativeY, relativeZ);
        var d010 = Dot(this.Permute(xy01 + z), relativeX, relativeY - 1.0f, relativeZ);
        var d110 = Dot(this.Permute(xy11 + z), relativeX - 1.0f, relativeY - 1.0f, relativeZ);
        var d001 = Dot(this.Permute(xy00 + z + 1), relativeX, relativeY, relativeZ - 1.0f);
        var d101 = Dot(this.Permute(xy10 + z + 1), relativeX - 1.0f, relativeY, relativeZ - 1.0f);
        var d011 = Dot(this.Permute(xy01 + z + 1), relativeX, relativeY - 1.0f, relativeZ - 1.0f);
        var d111 = Dot(this.Permute(xy11 + z + 1), relativeX - 1.0f, relativeY - 1.0f, relativeZ - 1.0f);

        var alphaX = FloatMath.Smoothstep(relativeX);
        var alphaY = FloatMath.Smoothstep(fadeY);
        var alphaZ = FloatMath.Smoothstep(relativeZ);

        var bottom = FloatMath.Lerp2(alphaX, alphaY, d000, d100, d010, d110);
        var top = FloatMath.Lerp2(alphaX, alphaY, d001, d101, d011, d111);

        return FloatMath.Lerp(alphaZ, bottom, top);
    }

    // The int gradient components widen to float before multiplying, as in vanilla; the sum order is vanilla's.
    private static float Dot(int hash, float x, float y, float z)
    {
        var gradient = gradients[hash & 15];
        return gradient.X * x + gradient.Y * y + gradient.Z * z;
    }

    private static double Wrap(double value)
    {
        if (value >= -halfWrapPeriod && value < halfWrapPeriod)
            return value;

        return value - Math.Floor(value / WrapPeriod + 0.5) * WrapPeriod;
    }
}

/// <summary>
/// The gradient noise of <c>old_blended_noise</c>, which quantises the y position inside a cell ("smearing").
/// </summary>
/// <remarks>Matches vanilla's SmearedPerlinNoise, including its double fudge and float epsilon.</remarks>
internal sealed class SmearedPerlinNoise(IRandomSource random, double fudgeYScale) : PerlinNoise(random)
{
    protected override float SampleCell(int floorX, int floorY, int floorZ, float relativeX, double relativeY,
        float relativeZ, double originalY)
    {
        var fudgeLimit = originalY >= 0.0 && originalY < relativeY ? originalY : relativeY;

        // 1.0E-7F is a float literal in vanilla, widened to double here.
        var fudge = Math.Floor(fudgeLimit / fudgeYScale + (double)1.0E-7f) * fudgeYScale;
        var fudgedY = (float)(relativeY - fudge);

        return this.SampleAndLerp(floorX, floorY, floorZ, relativeX, fudgedY, relativeZ, (float)relativeY);
    }
}
