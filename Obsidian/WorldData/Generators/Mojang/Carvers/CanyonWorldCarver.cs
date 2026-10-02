using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Generators.Mojang.Carvers;

/// <summary>
/// Ravines, mirroring vanilla's CanyonWorldCarver.
/// </summary>
/// <remarks>
/// The float/double mix is deliberate: vanilla computes angles and offsets in float and positions in double.
/// </remarks>
internal sealed class CanyonWorldCarver : WorldCarver<CanyonCarverConfiguration, CanyonWorldCarver.Canyon>
{
    public override Canyon Plan(CanyonCarverConfiguration configuration, IRandomSource random, int startChunkX, int startChunkZ, int minY, int height)
    {
        var maxDistance = (Range * 2 - 1) * 16;
        double x = (startChunkX << 4) + random.NextInt(16);
        var y = configuration.Y.Sample(random, minY, height);
        double z = (startChunkZ << 4) + random.NextInt(16);
        var yaw = random.NextFloat() * (float)(Math.PI * 2);
        var pitch = configuration.VerticalRotation.Sample(random);
        double yScale = configuration.YScale.Sample(random);
        var thickness = configuration.Thickness.Sample(random);
        var branchCount = (int)(maxDistance * configuration.DistanceFactor.Sample(random));

        return PlanCanyon(configuration, random.NextLong(), x, y, z, thickness, yaw, pitch, 0, branchCount, yScale, height);
    }

    public override void Carve(CarvingContext context, CanyonCarverConfiguration configuration, Canyon plan) =>
        this.CarveTunnel(context, configuration, plan.Tunnel, new SkipChecker(plan.WidthFactors, context.MinY));

    /// <summary>
    /// Follows the ravine (vanilla's <c>doCarve</c>) and records its ellipsoids; whether each one reaches a chunk is decided
    /// when carving.
    /// </summary>
    private static Canyon PlanCanyon(CanyonCarverConfiguration configuration, long seed, double x, double y, double z,
        float thickness, float yaw, float pitch, int branchIndex, int branchCount, double yScale, int height)
    {
        var random = new LegacyRandomSource(seed);
        var widthFactors = InitWidthFactors(height, configuration, random);
        var tunnel = new Tunnel(branchIndex, branchCount, thickness);
        var yawChange = 0.0f;
        var pitchChange = 0.0f;

        for (var index = branchIndex; index < branchCount; index++)
        {
            var horizontalRadius = 1.5 + Mth.Sin(index * (float)Math.PI / branchCount) * thickness;
            var verticalRadius = horizontalRadius * yScale;
            horizontalRadius *= configuration.HorizontalRadiusFactor.Sample(random);
            verticalRadius = UpdateVerticalRadius(configuration, random, verticalRadius, branchCount, index);

            var pitchCos = Mth.Cos(pitch);
            var pitchSin = Mth.Sin(pitch);
            x += Mth.Cos(yaw) * pitchCos;
            y += pitchSin;
            z += Mth.Sin(yaw) * pitchCos;

            pitch *= 0.7f;
            pitch += pitchChange * 0.05f;
            yaw += yawChange * 0.05f;
            pitchChange *= 0.8f;
            yawChange *= 0.5f;
            pitchChange += (random.NextFloat() - random.NextFloat()) * random.NextFloat() * 2.0f;
            yawChange += (random.NextFloat() - random.NextFloat()) * random.NextFloat() * 4.0f;

            if (random.NextInt(4) == 0)
                continue;

            tunnel.Steps.Add(new TunnelStep(index, x, y, z, horizontalRadius, verticalRadius));
        }

        return new Canyon(widthFactors, tunnel);
    }

    /// <summary>
    /// Per-Y width multipliers that give ravine walls their ledges.
    /// </summary>
    private static float[] InitWidthFactors(int height, CanyonCarverConfiguration configuration, IRandomSource random)
    {
        var factors = new float[height];
        var factor = 1.0f;

        for (var i = 0; i < factors.Length; i++)
        {
            if (i == 0 || random.NextInt(configuration.WidthSmoothness) == 0)
                factor = 1.0f + random.NextFloat() * random.NextFloat();

            factors[i] = factor * factor;
        }

        return factors;
    }

    private static double UpdateVerticalRadius(CanyonCarverConfiguration configuration, IRandomSource random, double verticalRadius,
        float branchCount, float index)
    {
        var centerFactor = 1.0f - Math.Abs(0.5f - index / branchCount) * 2.0f;
        var factor = configuration.VerticalRadiusDefaultFactor + configuration.VerticalRadiusCenterFactor * centerFactor;
        return factor * verticalRadius * (random.NextFloat() * (1.0f - 0.75f) + 0.75f);
    }

    /// <summary>
    /// A start chunk's ravine: its walls' width factors and its path.
    /// </summary>
    internal sealed record Canyon(float[] WidthFactors, Tunnel Tunnel);

    private readonly struct SkipChecker(float[] widthFactors, int minY) : ISkipChecker
    {
        public bool ShouldSkip(double relativeX, double relativeY, double relativeZ, int y) =>
            (relativeX * relativeX + relativeZ * relativeZ) * widthFactors[y - minY - 1] + relativeY * relativeY / 6.0 >= 1.0;

        // Width factors are at least 1, so they only narrow the bounds.
        public (double Min, double Max) RelativeYBounds(double relativeX, double relativeZ)
        {
            var halfHeight = Math.Sqrt(6.0 * (1.0 - (relativeX * relativeX + relativeZ * relativeZ)));
            return (-halfHeight, halfHeight);
        }
    }
}
