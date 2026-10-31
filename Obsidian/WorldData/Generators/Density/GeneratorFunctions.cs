namespace Obsidian.WorldData.Generators.Density;

/// <summary>
/// A constant (a number in JSON, or <c>constant</c>). Operators with a constant operand specialise on it, like
/// vanilla's compiler.
/// </summary>
internal sealed class ConstantFunction(float value) : DensityFunction
{
    public override bool IsFlat => true;

    public float Value { get; } = value;

    public override float Sample(int x, int y, int z) => this.Value;

    public override void SampleVolume(Span<float> output, in DensityVolume volume) => output.Fill(this.Value);
}

/// <summary>
/// A value the chunk being generated supplies (<c>blend_alpha</c>, <c>blend_offset</c>, <c>beardifier</c>), or its
/// default without one: 1 for blend_alpha, 0 for the others.
/// </summary>
/// <remarks>
/// Not a <see cref="ConstantFunction"/>: vanilla's operators don't specialise on these, which matters for the zero
/// check of a multiplication. Blending stays neutral; terrain can bind the beardifier through DensityCompiler.
/// </remarks>
internal sealed class ContextFunction(string name, float defaultValue) : DensityFunction
{
    public override bool IsFlat => true;

    public string Name { get; } = name;

    public override float Sample(int x, int y, int z) => defaultValue;
}

/// <summary>
/// <c>noise</c>: a noise at the block position times <c>xz_scale</c> and <c>y_scale</c>, offset by the optional
/// <c>shift_x</c>, <c>shift_y</c> and <c>shift_z</c> functions.
/// </summary>
/// <remarks>
/// Matches vanilla's three NoiseFunction samplers. Without shifts a volume goes through
/// <see cref="NoiseStack.AddToVolume"/>; with shifts each point is sampled at its shifted coordinates, which vanilla
/// computes alike for points and volumes.
/// </remarks>
internal sealed class NoiseFunction(
    NoiseStack noise,
    double xzScale,
    double yScale,
    DensityFunction? shiftX,
    DensityFunction? shiftY,
    DensityFunction? shiftZ) : DensityFunction
{
    public NoiseFunction(NoiseStack noise, double xzScale, double yScale)
        : this(noise, xzScale, yScale, null, null, null)
    {
    }

    // y times a zero scale is zero or negative zero, which noise reads alike.
    public override bool IsFlat => yScale == 0.0 && IsFlatShift(shiftX) && IsFlatShift(shiftY) && IsFlatShift(shiftZ);

    private bool Shifted => shiftX is not null || shiftY is not null || shiftZ is not null;

    private static bool IsFlatShift(DensityFunction? shift) => shift?.IsFlat ?? true;

    public override float Sample(int x, int y, int z)
    {
        if (!this.Shifted)
            return noise.Get(x * xzScale, y * yScale, z * xzScale);

        var noiseX = x * xzScale + Shift(shiftX, x, y, z);
        var noiseY = y * yScale + Shift(shiftY, x, y, z);
        var noiseZ = z * xzScale + Shift(shiftZ, x, y, z);

        return noise.Get(noiseX, noiseY, noiseZ);
    }

    public override void SampleVolume(Span<float> output, in DensityVolume volume)
    {
        if (!this.Shifted)
        {
            output.Fill(0.0f);
            noise.AddToVolume(output, volume, xzScale, yScale, 1.0f);
            return;
        }

        using var shiftsX = SampleShift(shiftX, volume);
        using var shiftsY = SampleShift(shiftY, volume);
        using var shiftsZ = SampleShift(shiftZ, volume);

        var index = 0;
        for (var indexZ = 0; indexZ < volume.SizeZ; indexZ++)
        {
            var baseZ = volume.BlockZ(indexZ) * xzScale;

            for (var indexX = 0; indexX < volume.SizeX; indexX++)
            {
                var baseX = volume.BlockX(indexX) * xzScale;

                for (var indexY = 0; indexY < volume.SizeY; indexY++)
                {
                    var noiseX = baseX + shiftsX.Span[index];
                    var noiseY = volume.BlockY(indexY) * yScale + shiftsY.Span[index];
                    var noiseZ = baseZ + shiftsZ.Span[index];

                    output[index] = noise.Get(noiseX, noiseY, noiseZ);
                    index++;
                }
            }
        }
    }

    // An absent shift is vanilla's zero constant: its float 0 added to the double coordinate.
    private static float Shift(DensityFunction? shift, int x, int y, int z) => shift?.Sample(x, y, z) ?? 0.0f;

    private static DensityBuffer SampleShift(DensityFunction? shift, in DensityVolume volume)
    {
        if (shift is not null)
            return DensityBuffer.Sample(shift, volume);

        var zeros = new DensityBuffer(volume.Size);
        zeros.Span.Fill(0.0f);

        return zeros;
    }
}

/// <summary>Which coordinates a shift noise reads (<c>shift</c>, <c>shift_a</c>, <c>shift_b</c>).</summary>
internal enum ShiftKind
{
    /// <summary><c>shift</c>: x, y and z.</summary>
    All,

    /// <summary><c>shift_a</c>: x and z, with y fixed at 0.</summary>
    A,

    /// <summary><c>shift_b</c>: z as x and x as y, with the third coordinate 0.</summary>
    B
}

/// <summary>
/// The shift noises that offset other noises' coordinates: a noise at a quarter of the block coordinates, times 4.
/// </summary>
/// <remarks>Matches vanilla's ShiftNoiseFunction samplers, including shift_b's transposed volume.</remarks>
internal sealed class ShiftFunction(NoiseStack noise, ShiftKind kind) : DensityFunction
{
    // shift and shift_a: the noise at a quarter of the coordinates (y fixed at 0 for shift_a). Unused by shift_b.
    private readonly NoiseFunction quarter = new(noise, 0.25, kind == ShiftKind.A ? 0.0 : 0.25);

    public override bool IsFlat => kind != ShiftKind.All;

    public override float Sample(int x, int y, int z)
    {
        if (kind == ShiftKind.B)
            return noise.Get(z * 0.25, x * 0.25, 0.0) * 4.0f;

        return this.quarter.Sample(x, y, z) * 4.0f;
    }

    public override void SampleVolume(Span<float> output, in DensityVolume volume)
    {
        if (kind != ShiftKind.B)
        {
            this.quarter.SampleVolume(output, volume);

            for (var i = 0; i < output.Length; i++)
                output[i] *= 4.0f;

            return;
        }

        // shift_b samples a z-by-x plane with z as the noise's x and x as its y, then repeats it down each column.
        var transposed = new DensityVolume(
            volume.SizeZ,
            volume.SizeX,
            1,
            volume.MinBlockZ,
            volume.MinBlockX,
            0,
            volume.StepZ,
            volume.StepX,
            1);

        using var plane = new DensityBuffer(transposed.Size);
        plane.Span.Fill(0.0f);
        noise.AddToVolume(plane.Span, transposed, 0.25, 0.25, 4.0f);

        for (var indexZ = 0; indexZ < volume.SizeZ; indexZ++)
        {
            for (var indexX = 0; indexX < volume.SizeX; indexX++)
            {
                var value = plane.Span[transposed.Index(indexZ, indexX, 0)];
                output.Slice(volume.Index(indexX, 0, indexZ), volume.SizeY).Fill(value);
            }
        }
    }
}

/// <summary>An axis a gradient or slice runs along.</summary>
internal enum DensityAxis
{
    X,
    Y,
    Z
}

/// <summary>How a gradient continues beyond its two coordinates (<c>tiling</c>).</summary>
internal enum GradientTiling
{
    /// <summary><c>clamp_to_edge</c>, the default: the end values continue.</summary>
    ClampToEdge,

    /// <summary><c>repeat</c>: the gradient starts over every range.</summary>
    Repeat,

    /// <summary><c>mirrored_repeat</c>: the gradient runs back and forth.</summary>
    MirroredRepeat
}

/// <summary>
/// <c>gradient</c>: from <c>from_value</c> at <c>from_coordinate</c> to <c>to_value</c> at <c>to_coordinate</c> along
/// an axis. Replaces <c>y_clamped_gradient</c>.
/// </summary>
/// <remarks>
/// Matches vanilla's GradientFunction samplers: the slope is a float divided by the int coordinate range, and the
/// value is the from value plus the int offset times the slope.
/// </remarks>
internal sealed class GradientFunction : DensityFunction
{
    private readonly DensityAxis axis;
    private readonly GradientTiling tiling;
    private readonly int fromCoordinate;
    private readonly int minCoordinate;
    private readonly int maxCoordinate;
    private readonly int coordinateRange;
    private readonly float fromValue;
    private readonly float slope;

    public GradientFunction(DensityAxis axis, GradientTiling tiling, int fromCoordinate, int toCoordinate,
        float fromValue, float toValue)
    {
        this.axis = axis;
        this.tiling = tiling;
        this.fromCoordinate = fromCoordinate;
        this.minCoordinate = Math.Min(fromCoordinate, toCoordinate);
        this.maxCoordinate = Math.Max(fromCoordinate, toCoordinate);
        this.coordinateRange = toCoordinate - fromCoordinate;
        this.fromValue = fromValue;
        this.slope = (toValue - fromValue) / this.coordinateRange;
    }

    public override bool IsFlat => this.axis != DensityAxis.Y;

    public override float Sample(int x, int y, int z)
    {
        var coordinate = Choose(this.axis, x, y, z);
        var relative = coordinate - this.fromCoordinate;

        switch (this.tiling)
        {
            case GradientTiling.ClampToEdge:
            {
                var clamped = Math.Clamp(coordinate, this.minCoordinate, this.maxCoordinate) - this.fromCoordinate;
                return this.fromValue + clamped * this.slope;
            }
            case GradientTiling.Repeat:
            {
                var local = FloatMath.FloorMod(relative, this.coordinateRange);
                return this.fromValue + local * this.slope;
            }
            default:
            {
                var tile = FloatMath.FloorDiv(relative, this.coordinateRange);
                var local = relative - tile * this.coordinateRange;
                var mirrored = (tile & 1) == 0 ? local : this.coordinateRange - local;

                return this.fromValue + mirrored * this.slope;
            }
        }
    }

    public static int Choose(DensityAxis axis, int x, int y, int z) => axis switch
    {
        DensityAxis.X => x,
        DensityAxis.Y => y,
        _ => z
    };
}

/// <summary>How <c>distance_to_point</c> measures (<c>metric</c>).</summary>
internal enum DistanceMetric
{
    Euclidean,
    EuclideanSquared,
    Manhattan,
    Chebyshev
}

/// <summary>
/// <c>distance_to_point</c>: the distance from a block to a fixed point, in float like vanilla's DistanceMetric.
/// </summary>
internal sealed class DistanceToPointFunction(int pointX, int pointY, int pointZ, DistanceMetric metric)
    : DensityFunction
{
    public override float Sample(int x, int y, int z)
    {
        float deltaX = pointX - x;
        float deltaY = pointY - y;
        float deltaZ = pointZ - z;

        switch (metric)
        {
            case DistanceMetric.Euclidean:
                return MathF.Sqrt(deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ);
            case DistanceMetric.EuclideanSquared:
                return deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ;
            case DistanceMetric.Manhattan:
                return MathF.Abs(deltaX) + MathF.Abs(deltaY) + MathF.Abs(deltaZ);
            default:
            {
                var horizontal = MathF.Max(MathF.Abs(deltaX), MathF.Abs(deltaY));
                return MathF.Max(horizontal, MathF.Abs(deltaZ));
            }
        }
    }
}
