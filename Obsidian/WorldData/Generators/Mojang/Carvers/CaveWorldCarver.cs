using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Generators.Mojang.Carvers;

/// <summary>
/// Classic winding cave tunnels with occasional rooms, mirroring vanilla's CaveWorldCarver.
/// </summary>
/// <remarks>
/// The float/double mix is deliberate: vanilla computes angles and offsets in float and positions in double.
/// </remarks>
internal sealed class CaveWorldCarver : WorldCarver<CaveCarverConfiguration>
{
    private const int CaveBound = 15;
    private const double YScaleMultiplier = 1.0;

    public override void Carve(CarvingContext context, CaveCarverConfiguration configuration, IRandomSource random, int startChunkX, int startChunkZ)
    {
        var maxDistance = (Range * 2 - 1) << 4;
        var caveCount = random.NextInt(random.NextInt(random.NextInt(CaveBound) + 1) + 1);

        for (var cave = 0; cave < caveCount; cave++)
        {
            double x = (startChunkX << 4) + random.NextInt(16);
            double y = configuration.Y.Sample(random, context.MinY, context.Height);
            double z = (startChunkZ << 4) + random.NextInt(16);
            double horizontalRadiusMultiplier = configuration.HorizontalRadiusMultiplier.Sample(random);
            double verticalRadiusMultiplier = configuration.VerticalRadiusMultiplier.Sample(random);
            double floorLevel = configuration.FloorLevel.Sample(random);

            CarveSkipChecker skipChecker = (relativeX, relativeY, relativeZ, _) => ShouldSkip(relativeX, relativeY, relativeZ, floorLevel);

            var tunnelCount = 1;
            if (random.NextInt(4) == 0)
            {
                double yScale = configuration.YScale.Sample(random);
                var thickness = 1.0f + random.NextFloat() * 6.0f;
                this.CreateRoom(context, configuration, x, y, z, thickness, yScale, skipChecker);
                tunnelCount += random.NextInt(4);
            }

            for (var tunnel = 0; tunnel < tunnelCount; tunnel++)
            {
                var yaw = random.NextFloat() * (float)(Math.PI * 2);
                var pitch = (random.NextFloat() - 0.5f) / 4.0f;
                var thickness = GetThickness(random);
                var branchCount = maxDistance - random.NextInt(maxDistance / 4);

                this.CreateTunnel(context, configuration, random.NextLong(), x, y, z, horizontalRadiusMultiplier, verticalRadiusMultiplier,
                    thickness, yaw, pitch, 0, branchCount, YScaleMultiplier, skipChecker);
            }
        }
    }

    private static float GetThickness(IRandomSource random)
    {
        var thickness = random.NextFloat() * 2.0f + random.NextFloat();

        if (random.NextInt(10) == 0)
            thickness *= random.NextFloat() * random.NextFloat() * 3.0f + 1.0f;

        return thickness;
    }

    private void CreateRoom(CarvingContext context, CaveCarverConfiguration configuration, double x, double y, double z,
        float thickness, double yScale, CarveSkipChecker skipChecker)
    {
        var horizontalRadius = 1.5 + Mth.Sin((float)(Math.PI / 2)) * thickness;
        var verticalRadius = horizontalRadius * yScale;

        this.CarveEllipsoid(context, configuration, x + 1.0, y, z, horizontalRadius, verticalRadius, skipChecker);
    }

    private void CreateTunnel(CarvingContext context, CaveCarverConfiguration configuration, long seed, double x, double y, double z,
        double horizontalRadiusMultiplier, double verticalRadiusMultiplier, float thickness, float yaw, float pitch,
        int branchIndex, int branchCount, double yScale, CarveSkipChecker skipChecker)
    {
        var random = new LegacyRandomSource(seed);
        var splitIndex = random.NextInt(branchCount / 2) + branchCount / 4;
        var steep = random.NextInt(6) == 0;
        var yawChange = 0.0f;
        var pitchChange = 0.0f;

        for (var index = branchIndex; index < branchCount; index++)
        {
            var horizontalRadius = 1.5 + Mth.Sin((float)Math.PI * index / branchCount) * thickness;
            var verticalRadius = horizontalRadius * yScale;
            var pitchCos = Mth.Cos(pitch);

            x += Mth.Cos(yaw) * pitchCos;
            y += Mth.Sin(pitch);
            z += Mth.Sin(yaw) * pitchCos;

            pitch *= steep ? 0.92f : 0.7f;
            pitch += pitchChange * 0.1f;
            yaw += yawChange * 0.1f;
            pitchChange *= 0.9f;
            yawChange *= 0.75f;
            pitchChange += (random.NextFloat() - random.NextFloat()) * random.NextFloat() * 2.0f;
            yawChange += (random.NextFloat() - random.NextFloat()) * random.NextFloat() * 4.0f;

            if (index == splitIndex && thickness > 1.0f)
            {
                this.CreateTunnel(context, configuration, random.NextLong(), x, y, z, horizontalRadiusMultiplier, verticalRadiusMultiplier,
                    random.NextFloat() * 0.5f + 0.5f, yaw - (float)(Math.PI / 2), pitch / 3.0f, index, branchCount, 1.0, skipChecker);
                this.CreateTunnel(context, configuration, random.NextLong(), x, y, z, horizontalRadiusMultiplier, verticalRadiusMultiplier,
                    random.NextFloat() * 0.5f + 0.5f, yaw + (float)(Math.PI / 2), pitch / 3.0f, index, branchCount, 1.0, skipChecker);
                return;
            }

            if (random.NextInt(4) == 0)
                continue;

            if (!CanReach(context.Chunk, x, z, index, branchCount, thickness))
                return;

            this.CarveEllipsoid(context, configuration, x, y, z, horizontalRadius * horizontalRadiusMultiplier,
                verticalRadius * verticalRadiusMultiplier, skipChecker);
        }
    }

    private static bool ShouldSkip(double relativeX, double relativeY, double relativeZ, double floorLevel) =>
        relativeY <= floorLevel || relativeX * relativeX + relativeY * relativeY + relativeZ * relativeZ >= 1.0;
}
