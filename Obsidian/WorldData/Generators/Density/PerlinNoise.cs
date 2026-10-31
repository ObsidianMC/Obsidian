using Obsidian.API.World.Generator.RandomSources;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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

    protected int Permute(int value) => Permute(ref this.Permutations, value);

    /// <summary>
    /// The first of the 256 permuted values, for lookups without bounds checks (see
    /// <see cref="Permute(ref byte, int)"/>).
    /// </summary>
    protected ref byte Permutations => ref MemoryMarshal.GetArrayDataReference(this.permutations);

    // Masking keeps the index inside the 256 values, so the lookup skips the bounds check: noise samples look up
    // the permutation 14 times per octave.
    protected static int Permute(ref byte permutations, int value) => Unsafe.Add(ref permutations, value & 0xFF);
}

/// <summary>
/// One octave of 26.3's gradient noise, sampled in single precision.
/// </summary>
/// <remarks>
/// Matches vanilla's PerlinNoise bit for bit: the coordinates are wrapped and offset in double, the position inside the
/// lattice cell is narrowed to float, and the gradients, fade curve and interpolation are float.
/// </remarks>
internal partial class PerlinNoise : GradientNoise
{
    // Coordinates are wrapped into vanilla's range of 2^25 so that large ones keep their precision (no far lands).
    private const double WrapPeriod = 33554432.0;
    private static readonly double halfWrapPeriod = Math.BitDecrement(16777216.0);

    // The gradients with float components, four floats apart: vanilla widens each int component before multiplying,
    // so multiplying by these rounds the same.
    private static readonly float[] floatGradients = CreateFloatGradients();

    // The smeared variant's y quantum, or 0. A field rather than an override keeps the octave loop free of virtual
    // calls; every noise layer samples through here.
    private readonly double smearScaleY;

    // The permuted values as ints, which vector lookups gather.
    private readonly int[] permutationTable = new int[256];

    public PerlinNoise(IRandomSource random)
        : this(random, 0.0)
    {
    }

    protected PerlinNoise(IRandomSource random, double smearScaleY)
        : base(random, 256.0)
    {
        this.smearScaleY = smearScaleY;

        for (var i = 0; i < this.permutationTable.Length; i++)
            this.permutationTable[i] = this.Permute(i);
    }

    /// <summary>The noise at a point.</summary>
    public float Get(double x, double y, double z)
    {
        var wrappedX = Wrap(x) + this.OffsetX;
        var wrappedY = Wrap(y) + this.OffsetY;
        var wrappedZ = Wrap(z) + this.OffsetZ;

        var floorX = (int)Math.Floor(wrappedX);
        var floorY = (int)Math.Floor(wrappedY);
        var floorZ = (int)Math.Floor(wrappedZ);

        var relativeX = (float)(wrappedX - floorX);
        var relativeY = wrappedY - floorY;
        var relativeZ = (float)(wrappedZ - floorZ);

        if (this.smearScaleY == 0.0)
        {
            var narrowedY = (float)relativeY;
            return this.SampleAndLerp(floorX, floorY, floorZ, relativeX, narrowedY, relativeZ, narrowedY);
        }

        return this.SampleSmeared(floorX, floorY, floorZ, relativeX, relativeY, relativeZ, y);
    }

    /// <summary>
    /// Vanilla's SmearedPerlinNoise: the y position inside the cell is quantised before narrowing, with its double
    /// fudge and float epsilon, while the fade still follows the unquantised position.
    /// </summary>
    /// <param name="originalY">The y coordinate before wrapping and offsetting.</param>
    private float SampleSmeared(int floorX, int floorY, int floorZ, float relativeX, double relativeY,
        float relativeZ, double originalY)
    {
        var fudgeLimit = originalY >= 0.0 && originalY < relativeY ? originalY : relativeY;

        // 1.0E-7F is a float literal in vanilla, widened to double here.
        var fudge = Math.Floor(fudgeLimit / this.smearScaleY + (double)1.0E-7f) * this.smearScaleY;
        var fudgedY = (float)(relativeY - fudge);

        return this.SampleAndLerp(floorX, floorY, floorZ, relativeX, fudgedY, relativeZ, (float)relativeY);
    }

    /// <summary>
    /// Vanilla's trilinear blend of the corner gradients. <paramref name="relativeY"/> places the corners and
    /// <paramref name="fadeY"/> drives the y fade; they differ only for smeared noise.
    /// </summary>
    protected float SampleAndLerp(int x, int y, int z, float relativeX, float relativeY, float relativeZ, float fadeY)
    {
        ref var permutations = ref this.Permutations;
        var x0 = Permute(ref permutations, x);
        var x1 = Permute(ref permutations, x + 1);
        var xy00 = Permute(ref permutations, x0 + y);
        var xy01 = Permute(ref permutations, x0 + y + 1);
        var xy10 = Permute(ref permutations, x1 + y);
        var xy11 = Permute(ref permutations, x1 + y + 1);

        var d000 = Dot(Permute(ref permutations, xy00 + z), relativeX, relativeY, relativeZ);
        var d100 = Dot(Permute(ref permutations, xy10 + z), relativeX - 1.0f, relativeY, relativeZ);
        var d010 = Dot(Permute(ref permutations, xy01 + z), relativeX, relativeY - 1.0f, relativeZ);
        var d110 = Dot(Permute(ref permutations, xy11 + z), relativeX - 1.0f, relativeY - 1.0f, relativeZ);
        var d001 = Dot(Permute(ref permutations, xy00 + z + 1), relativeX, relativeY, relativeZ - 1.0f);
        var d101 = Dot(Permute(ref permutations, xy10 + z + 1), relativeX - 1.0f, relativeY, relativeZ - 1.0f);
        var d011 = Dot(Permute(ref permutations, xy01 + z + 1), relativeX, relativeY - 1.0f, relativeZ - 1.0f);
        var d111 = Dot(Permute(ref permutations, xy11 + z + 1), relativeX - 1.0f, relativeY - 1.0f, relativeZ - 1.0f);

        var alphaX = FloatMath.Smoothstep(relativeX);
        var alphaY = FloatMath.Smoothstep(fadeY);
        var alphaZ = FloatMath.Smoothstep(relativeZ);

        var bottom = FloatMath.Lerp2(alphaX, alphaY, d000, d100, d010, d110);
        var top = FloatMath.Lerp2(alphaX, alphaY, d001, d101, d011, d111);

        return FloatMath.Lerp(alphaZ, bottom, top);
    }

    // The sum order is vanilla's.
    private static float Dot(int hash, float x, float y, float z)
    {
        // Masked like Permute, so without a bounds check.
        ref var gradient = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(floatGradients), (hash & 15) << 2);
        return gradient * x + Unsafe.Add(ref gradient, 1) * y + Unsafe.Add(ref gradient, 2) * z;
    }

    private static float[] CreateFloatGradients()
    {
        var components = new float[gradients.Length * 4];

        for (var i = 0; i < gradients.Length; i++)
        {
            components[i * 4] = gradients[i].X;
            components[i * 4 + 1] = gradients[i].Y;
            components[i * 4 + 2] = gradients[i].Z;
        }

        return components;
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
/// <param name="fudgeYScale">The y quantum; positive, as <c>old_blended_noise</c>'s scales are.</param>
internal sealed class SmearedPerlinNoise(IRandomSource random, double fudgeYScale) : PerlinNoise(random, fudgeYScale);
