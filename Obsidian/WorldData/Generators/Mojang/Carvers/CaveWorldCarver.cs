using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Generators.Mojang.Carvers;

/// <summary>
/// Classic winding cave tunnels with occasional rooms, mirroring vanilla's CaveWorldCarver.
/// </summary>
/// <remarks>
/// The float/double mix is deliberate: vanilla computes angles and offsets in float and positions in double.
/// </remarks>
internal class CaveWorldCarver : WorldCarver<CaveCarverConfiguration, CaveWorldCarver.Cave[]>
{
    /// <summary>
    /// Bounds the number of caves a start chunk carves.
    /// </summary>
    protected virtual int CaveBound => 15;

    /// <summary>
    /// Vertical stretch of the tunnels.
    /// </summary>
    protected virtual double YScale => 1.0;

    public override Cave[] Plan(CaveCarverConfiguration configuration, IRandomSource random, int startChunkX, int startChunkZ, int minY, int height)
    {
        var maxDistance = (Range * 2 - 1) << 4;
        var caves = new Cave[random.NextInt(random.NextInt(random.NextInt(this.CaveBound) + 1) + 1)];

        for (var cave = 0; cave < caves.Length; cave++)
        {
            double x = (startChunkX << 4) + random.NextInt(16);
            double y = configuration.Y.Sample(random, minY, height);
            double z = (startChunkZ << 4) + random.NextInt(16);
            double horizontalRadiusMultiplier = configuration.HorizontalRadiusMultiplier.Sample(random);
            double verticalRadiusMultiplier = configuration.VerticalRadiusMultiplier.Sample(random);
            double floorLevel = configuration.FloorLevel.Sample(random);

            TunnelStep? room = null;
            var tunnelCount = 1;
            if (random.NextInt(4) == 0)
            {
                double yScale = configuration.YScale.Sample(random);
                var thickness = 1.0f + random.NextFloat() * 6.0f;
                var horizontalRadius = 1.5 + Mth.Sin((float)(Math.PI / 2)) * thickness;
                room = new TunnelStep(0, x + 1.0, y, z, horizontalRadius, horizontalRadius * yScale);
                tunnelCount += random.NextInt(4);
            }

            var tunnels = new Tunnel[tunnelCount];
            for (var tunnel = 0; tunnel < tunnelCount; tunnel++)
            {
                var yaw = random.NextFloat() * (float)(Math.PI * 2);
                var pitch = (random.NextFloat() - 0.5f) / 4.0f;
                var thickness = this.GetThickness(random);
                var branchCount = maxDistance - random.NextInt(maxDistance / 4);

                tunnels[tunnel] = this.PlanTunnel(random.NextLong(), x, y, z, horizontalRadiusMultiplier, verticalRadiusMultiplier,
                    thickness, yaw, pitch, 0, branchCount, this.YScale);
            }

            caves[cave] = new Cave(new SkipChecker(floorLevel), room, tunnels);
        }

        return caves;
    }

    public override void Carve(CarvingContext context, CaveCarverConfiguration configuration, Cave[] plan)
    {
        foreach (var cave in plan)
        {
            if (cave.Room is { } room)
                this.CarveEllipsoid(context, configuration, room.X, room.Y, room.Z, room.HorizontalRadius, room.VerticalRadius, cave.SkipChecker);

            foreach (var tunnel in cave.Tunnels)
                this.CarveTunnel(context, configuration, tunnel, cave.SkipChecker);
        }
    }

    /// <summary>
    /// Draws a tunnel's thickness.
    /// </summary>
    protected virtual float GetThickness(IRandomSource random)
    {
        var thickness = random.NextFloat() * 2.0f + random.NextFloat();

        if (random.NextInt(10) == 0)
            thickness *= random.NextFloat() * random.NextFloat() * 3.0f + 1.0f;

        return thickness;
    }

    /// <summary>
    /// Follows a tunnel (vanilla's <c>createTunnel</c>) and records its ellipsoids; whether each one reaches a chunk is decided
    /// when carving.
    /// </summary>
    private Tunnel PlanTunnel(long seed, double x, double y, double z, double horizontalRadiusMultiplier, double verticalRadiusMultiplier,
        float thickness, float yaw, float pitch, int branchIndex, int branchCount, double yScale)
    {
        var tunnel = new Tunnel(branchIndex, branchCount, thickness);
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
                tunnel.Branches = (
                    this.PlanTunnel(random.NextLong(), x, y, z, horizontalRadiusMultiplier, verticalRadiusMultiplier,
                        random.NextFloat() * 0.5f + 0.5f, yaw - (float)(Math.PI / 2), pitch / 3.0f, index, branchCount, 1.0),
                    this.PlanTunnel(random.NextLong(), x, y, z, horizontalRadiusMultiplier, verticalRadiusMultiplier,
                        random.NextFloat() * 0.5f + 0.5f, yaw + (float)(Math.PI / 2), pitch / 3.0f, index, branchCount, 1.0));
                return tunnel;
            }

            if (random.NextInt(4) == 0)
                continue;

            tunnel.Steps.Add(new TunnelStep(index, x, y, z, horizontalRadius * horizontalRadiusMultiplier, verticalRadius * verticalRadiusMultiplier));
        }

        return tunnel;
    }

    /// <summary>
    /// One of a start chunk's caves: its room, if any, and its tunnels, carved in that order.
    /// </summary>
    internal sealed record Cave(SkipChecker SkipChecker, TunnelStep? Room, Tunnel[] Tunnels);

    internal readonly struct SkipChecker(double floorLevel) : ISkipChecker
    {
        public bool ShouldSkip(double relativeX, double relativeY, double relativeZ, int y) =>
            relativeY <= floorLevel || relativeX * relativeX + relativeY * relativeY + relativeZ * relativeZ >= 1.0;

        public (double Min, double Max) RelativeYBounds(double relativeX, double relativeZ)
        {
            var halfHeight = Math.Sqrt(1.0 - (relativeX * relativeX + relativeZ * relativeZ));
            return (Math.Max(floorLevel, -halfHeight), halfHeight);
        }
    }
}
