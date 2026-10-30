using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Mojang;

namespace Obsidian.WorldData.Generators.Terrain;

/// <summary>
/// Underground lakes and lava pockets with their own fluid levels, separated by barriers.
/// </summary>
/// <remarks>
/// Matches 26.3 aquifer decisions and double pressure arithmetic. Centers sit on a jittered 16x12x16 grid;
/// each position blends the fluid status of its nearest centers. Instances cache per chunk and aren't thread-safe.
/// </remarks>
internal sealed class TerrainAquifer
{
    private const int XRange = 10;
    private const int YRange = 9;
    private const int ZRange = 10;
    private const int WayBelowMinY = -32512;

    private static readonly double flowingUpdateSimilarity = Similarity(10 * 10, 12 * 12);

    // Chunk offsets, in chunks, of the preliminary surface samples around an aquifer center.
    private static readonly (int X, int Z)[] surfaceSamplingOffsets =
    [
        (0, 0), (-2, -1), (-1, -1), (0, -1), (1, -1), (-3, 0), (-2, 0),
        (-1, 0), (1, 0), (-2, 1), (-1, 1), (0, 1), (1, 1)
    ];

    private readonly ImmutableDictionary<string, DensityFunction>? functions;
    private readonly IPositionalRandomFactory random;
    private readonly Dictionary<(int X, int Z), int> surfaceLevels = [];
    private readonly FluidPicker globalFluidPicker;
    private readonly FluidStatus?[] aquiferCache = [];
    private readonly (int X, int Y, int Z)?[] aquiferLocationCache = [];
    private readonly int minGridY;
    private readonly int gridSizeX;
    private readonly int gridSizeZ;
    private readonly int skipSamplingAboveY;
    private readonly int minGridX;
    private readonly int minGridZ;

    // See GetCandidates.
    private readonly Candidate[] candidates = new Candidate[12];
    private int candidatesGridX = int.MinValue;
    private int candidatesGridY;
    private int candidatesGridZ;

    public bool ShouldScheduleFluidUpdate { get; private set; }

    public TerrainAquifer(TerrainContext context)
    {
        this.functions = context.Generator.Router.Aquifers;
        this.random = context.Generator.Compiler.RandomFactory("minecraft:aquifer");
        this.globalFluidPicker = Aquifers.CreateGlobalFluidPicker(
            context.Generator.Settings.SeaLevel,
            context.Generator.DefaultFluid);
        this.skipSamplingAboveY = int.MinValue;

        if (this.functions is null)
        {
            return;
        }

        var volume = context.Volume;
        this.minGridX = GridX(volume.MinBlockX - 5);
        this.minGridY = GridY(volume.MinBlockY + 1) - 1;
        this.minGridZ = GridZ(volume.MinBlockZ - 5);
        var maxGridX = GridX(volume.MinBlockX + 15 - 5) + 1;
        var maxGridY = GridY(volume.MinBlockY + volume.SizeY) + 1;
        var maxGridZ = GridZ(volume.MinBlockZ + 15 - 5) + 1;
        this.gridSizeX = maxGridX - this.minGridX + 1;
        this.gridSizeZ = maxGridZ - this.minGridZ + 1;
        var size = this.gridSizeX * (maxGridY - this.minGridY + 1) * this.gridSizeZ;
        this.aquiferCache = new FluidStatus?[size];
        this.aquiferLocationCache = new (int, int, int)?[size];

        var maxSurface = this.MaxSurfaceLevel(
            FromGridX(this.minGridX, 0),
            FromGridZ(this.minGridZ, 0),
            FromGridX(maxGridX, 9),
            FromGridZ(maxGridZ, 9));
        var surface = AdjustSurfaceLevel(maxSurface);

        // 26.3 skips sampling above the lowest candidate grid layer beyond the maximum surface.
        this.skipSamplingAboveY = FromGridY(GridY(surface + 12) - 1, 11) - 1;
    }

    private float Sample(string name, int x, int y, int z) => this.functions![name].Sample(x, y, z);

    private int SurfaceLevel(int x, int z)
    {
        var key = (x & ~3, z & ~3);

        if (!this.surfaceLevels.TryGetValue(key, out var level))
        {
            level = (int)MathF.Floor(this.Sample("surface_level", key.Item1, 0, key.Item2));
            this.surfaceLevels.Add(key, level);
        }

        return level;
    }

    private int MaxSurfaceLevel(int minX, int minZ, int maxX, int maxZ)
    {
        var volume = new DensityVolume(
            (maxX >> 2) - (minX >> 2) + 1,
            1,
            (maxZ >> 2) - (minZ >> 2) + 1,
            minX & ~3,
            0,
            minZ & ~3,
            4,
            1,
            4);
        using var values = DensityBuffer.Sample(this.functions!["surface_level"], volume);
        var maximum = int.MinValue;

        for (var z = 0; z < volume.SizeZ; z++)
        {
            for (var x = 0; x < volume.SizeX; x++)
            {
                var level = (int)MathF.Floor(values.Span[volume.Index(x, 0, z)]);
                this.surfaceLevels[(volume.BlockX(x), volume.BlockZ(z))] = level;
                maximum = Math.Max(maximum, level);
            }
        }

        return maximum;
    }

    public IBlock? ComputeSubstance(int x, int y, int z, double density)
    {
        if (density > 0.0)
        {
            this.ShouldScheduleFluidUpdate = false;
            return null;
        }

        var globalFluid = this.globalFluidPicker(x, y, z);

        if (y > this.skipSamplingAboveY)
        {
            this.ShouldScheduleFluidUpdate = false;
            return globalFluid.At(y);
        }

        if (globalFluid.BlockIdAt(y) == BlockIds.Lava)
        {
            this.ShouldScheduleFluidUpdate = false;
            return BlocksRegistry.Lava;
        }

        var gridX = GridX(x - 5);
        var gridY = GridY(y + 1);
        var gridZ = GridZ(z - 5);

        // Equal distances choose the later candidate in vanilla's x, y, z search order.
        var candidates = this.GetCandidates(gridX, gridY, gridZ);
        Span<int> nearest = stackalloc int[4];
        nearest.Fill(int.MaxValue);

        for (var i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i];
            var dx = candidate.X - x;
            var dy = candidate.Y - y;
            var dz = candidate.Z - z;
            var key = (dx * dx + dy * dy + dz * dz) << 4 | (15 - i);

            for (var slot = 0; slot < nearest.Length; slot++)
            {
                if (key >= nearest[slot])
                {
                    continue;
                }

                var displaced = nearest[slot];
                nearest[slot] = key;
                key = displaced;
            }
        }

        var distance1 = nearest[0] >> 4;
        var distance2 = nearest[1] >> 4;
        var distance3 = nearest[2] >> 4;
        var distance4 = nearest[3] >> 4;
        var index1 = candidates[15 - (nearest[0] & 15)].Index;
        var index2 = candidates[15 - (nearest[1] & 15)].Index;
        var index3 = candidates[15 - (nearest[2] & 15)].Index;
        var index4 = candidates[15 - (nearest[3] & 15)].Index;

        var closest = this.GetAquiferStatus(index1);
        var similarity12 = Similarity(distance1, distance2);
        var block = closest.At(y);

        if (similarity12 <= 0.0)
        {
            this.ShouldScheduleFluidUpdate = similarity12 >= flowingUpdateSimilarity
                && closest != this.GetAquiferStatus(index2);
            return block;
        }

        if (closest.BlockIdAt(y) == BlockIds.Water
            && this.globalFluidPicker(x, y - 1, z).BlockIdAt(y - 1) == BlockIds.Lava)
        {
            this.ShouldScheduleFluidUpdate = true;
            return block;
        }

        // The barrier noise is sampled at most once per position and shared by the pressure checks.
        var barrier = double.NaN;
        var second = this.GetAquiferStatus(index2);

        if (density + similarity12 * this.CalculatePressure(x, y, z, ref barrier, closest, second) > 0.0)
        {
            this.ShouldScheduleFluidUpdate = false;
            return null;
        }

        var third = this.GetAquiferStatus(index3);
        var similarity13 = Similarity(distance1, distance3);

        var thirdBlocks = similarity13 > 0.0
            && density + similarity12 * similarity13
                * this.CalculatePressure(x, y, z, ref barrier, closest, third) > 0.0;

        if (thirdBlocks)
        {
            this.ShouldScheduleFluidUpdate = false;
            return null;
        }

        var similarity23 = Similarity(distance2, distance3);

        var betweenBlocks = similarity23 > 0.0
            && density + similarity12 * similarity23
                * this.CalculatePressure(x, y, z, ref barrier, second, third) > 0.0;

        if (betweenBlocks)
        {
            this.ShouldScheduleFluidUpdate = false;
            return null;
        }

        var closestDiffersFromSecond = closest != second;
        var secondDiffersFromThird = similarity23 >= flowingUpdateSimilarity && second != third;
        var closestDiffersFromThird = similarity13 >= flowingUpdateSimilarity && closest != third;

        this.ShouldScheduleFluidUpdate = closestDiffersFromSecond || secondDiffersFromThird || closestDiffersFromThird
            || (similarity13 >= flowingUpdateSimilarity
                && Similarity(distance1, distance4) >= flowingUpdateSimilarity
                && closest != this.GetAquiferStatus(index4));

        return block;
    }

    private static double Similarity(int firstDistance, int secondDistance) =>
        1.0 - (secondDistance - firstDistance) / 25.0;

    private double CalculatePressure(int x, int y, int z, ref double barrier, FluidStatus first, FluidStatus second)
    {
        var firstBlock = first.BlockIdAt(y);
        var secondBlock = second.BlockIdAt(y);

        if ((firstBlock == BlockIds.Lava && secondBlock == BlockIds.Water)
            || (firstBlock == BlockIds.Water && secondBlock == BlockIds.Lava))
        {
            return 2.0;
        }

        var levelDifference = Math.Abs(first.FluidLevel - second.FluidLevel);
        if (levelDifference == 0)
        {
            return 0.0;
        }

        var middle = 0.5 * (first.FluidLevel + second.FluidLevel);
        var offsetFromMiddle = y + 0.5 - middle;
        var halfDifference = levelDifference / 2.0;
        var distanceToEdge = halfDifference - Math.Abs(offsetFromMiddle);

        double pressure;
        if (offsetFromMiddle > 0.0)
        {
            var value = 0.0 + distanceToEdge;
            pressure = value > 0.0 ? value / 1.5 : value / 2.5;
        }
        else
        {
            var value = 3.0 + distanceToEdge;
            pressure = value > 0.0 ? value / 3.0 : value / 10.0;
        }

        double barrierValue = 0.0;
        if (!(pressure < -2.0) && !(pressure > 2.0))
        {
            if (double.IsNaN(barrier))
            {
                barrier = this.Sample("barrier", x, y, z);
            }

            barrierValue = barrier;
        }

        return 2.0 * (barrierValue + pressure);
    }

    /// <summary>
    /// The 12 aquifer centers around the given grid cell, in search order. Neighboring blocks almost always share
    /// a cell, so the last cell's centers are kept.
    /// </summary>
    private ReadOnlySpan<Candidate> GetCandidates(int gridX, int gridY, int gridZ)
    {
        if (gridX == this.candidatesGridX && gridY == this.candidatesGridY && gridZ == this.candidatesGridZ)
        {
            return this.candidates;
        }

        var i = 0;
        for (var offsetX = 0; offsetX <= 1; offsetX++)
        {
            for (var offsetY = -1; offsetY <= 1; offsetY++)
            {
                for (var offsetZ = 0; offsetZ <= 1; offsetZ++)
                {
                    var index = this.GetIndex(gridX + offsetX, gridY + offsetY, gridZ + offsetZ);
                    var (x, y, z) = this.GetAquiferLocation(index, gridX + offsetX, gridY + offsetY, gridZ + offsetZ);
                    this.candidates[i++] = new Candidate(index, x, y, z);
                }
            }
        }

        (this.candidatesGridX, this.candidatesGridY, this.candidatesGridZ) = (gridX, gridY, gridZ);
        return this.candidates;
    }

    private int GetIndex(int gridX, int gridY, int gridZ) =>
        ((gridY - this.minGridY) * this.gridSizeZ + (gridZ - this.minGridZ)) * this.gridSizeX + (gridX - this.minGridX);

    private (int X, int Y, int Z) GetAquiferLocation(int index, int gridX, int gridY, int gridZ)
    {
        if (this.aquiferLocationCache[index] is ValueTuple<int, int, int> cached)
        {
            return cached;
        }

        var random = new PositionalRandom(this.random, gridX, gridY, gridZ);
        var location = (
            FromGridX(gridX, random.NextInt(XRange)),
            FromGridY(gridY, random.NextInt(YRange)),
            FromGridZ(gridZ, random.NextInt(ZRange)));

        this.aquiferLocationCache[index] = location;
        return location;
    }

    private FluidStatus GetAquiferStatus(int index)
    {
        if (this.aquiferCache[index] is FluidStatus cached)
        {
            return cached;
        }

        var (x, y, z) = this.aquiferLocationCache[index]!.Value;
        var status = this.FluidAt(x, y, z);

        this.aquiferCache[index] = status;
        return status;
    }

    public FluidStatus FluidAt(int x, int y, int z)
    {
        var globalFluid = this.globalFluidPicker(x, y, z);
        var lowestPreliminarySurface = int.MaxValue;
        var top = y + 12;
        var bottom = y - 12;
        var surfaceIsFlooded = false;

        foreach (var (offsetX, offsetZ) in surfaceSamplingOffsets)
        {
            var sampleX = x + (offsetX << 4);
            var sampleZ = z + (offsetZ << 4);
            var preliminarySurface = this.SurfaceLevel(sampleX, sampleZ);
            var surface = AdjustSurfaceLevel(preliminarySurface);
            var isCenter = offsetX == 0 && offsetZ == 0;

            if (isCenter && bottom > surface)
            {
                return globalFluid;
            }

            var surfaceBelowTop = top > surface;
            if (surfaceBelowTop || isCenter)
            {
                var surfaceFluid = this.globalFluidPicker(sampleX, surface, sampleZ);
                if (!surfaceFluid.At(surface).IsAir)
                {
                    if (isCenter)
                    {
                        surfaceIsFlooded = true;
                    }

                    if (surfaceBelowTop)
                    {
                        return surfaceFluid;
                    }
                }
            }

            lowestPreliminarySurface = Math.Min(lowestPreliminarySurface, preliminarySurface);
        }

        var fluidLevel = this.ComputeSurfaceLevel(x, y, z, globalFluid, lowestPreliminarySurface, surfaceIsFlooded);
        return new FluidStatus(fluidLevel, this.ComputeFluidType(x, y, z, globalFluid, fluidLevel));
    }

    private static int AdjustSurfaceLevel(int level) => level + 8;

    private int ComputeSurfaceLevel(
        int x,
        int y,
        int z,
        FluidStatus globalFluid,
        int lowestPreliminarySurface,
        bool surfaceIsFlooded)
    {
        double partiallyFlooded;
        double fullyFlooded;

        // Deep dark regions stay dry.
        if (this.Sample("exclusion", x, y, z) > 0.0f)
        {
            partiallyFlooded = -1.0;
            fullyFlooded = -1.0;
        }
        else
        {
            var distanceBelowSurface = lowestPreliminarySurface + 8 - y;
            var surfaceProximity = surfaceIsFlooded ? ClampedMap(distanceBelowSurface, 0.0, 64.0, 1.0, 0.0) : 0.0;
            var floodedness = Math.Clamp(this.Sample("fluid_level_floodedness", x, y, z), -1.0, 1.0);
            var fullyFloodedThreshold = Map(surfaceProximity, 1.0, 0.0, -0.3, 0.8);
            var partiallyFloodedThreshold = Map(surfaceProximity, 1.0, 0.0, -0.8, 0.4);

            partiallyFlooded = floodedness - partiallyFloodedThreshold;
            fullyFlooded = floodedness - fullyFloodedThreshold;
        }

        if (fullyFlooded > 0.0)
        {
            return globalFluid.FluidLevel;
        }

        if (partiallyFlooded > 0.0)
        {
            return this.ComputeRandomizedFluidSurfaceLevel(x, y, z, lowestPreliminarySurface);
        }

        return WayBelowMinY;
    }

    private int ComputeRandomizedFluidSurfaceLevel(int x, int y, int z, int lowestPreliminarySurface)
    {
        var cellX = (int)Math.Floor(x / 16.0);
        var cellY = (int)Math.Floor(y / 40.0);
        var cellZ = (int)Math.Floor(z / 16.0);
        var cellMiddleY = cellY * 40 + 20;

        // Vanilla samples the spread noise at the cell indices, not at block coordinates. It scales the float sample by
        // 10.0f before widening, so the product rounds to a float first: -0.3f * 10.0f is exactly -3.0f, where the
        // double product is just below it and quantizes a whole step lower.
        var spread = this.Sample("fluid_level_spread", cellX, cellY, cellZ) * 10.0f;
        var quantizedSpread = (int)Math.Floor(spread / 3.0) * 3;

        return Math.Min(lowestPreliminarySurface, cellMiddleY + quantizedSpread);
    }

    private IBlock ComputeFluidType(int x, int y, int z, FluidStatus globalFluid, int fluidLevel)
    {
        if (fluidLevel > -10 || fluidLevel == WayBelowMinY || globalFluid.FluidType.Is(BlockIds.Lava))
        {
            return globalFluid.FluidType;
        }

        // Like the spread noise, the lava noise is sampled at cell indices.
        var lava = this.functions!["lava"].Sample(
            (int)Math.Floor(x / 64.0),
            (int)Math.Floor(y / 40.0),
            (int)Math.Floor(z / 64.0));
        return Math.Abs(lava) > 0.3 ? BlocksRegistry.Lava : globalFluid.FluidType;
    }

    private static double ClampedMap(double value, double fromMin, double fromMax, double toMin, double toMax)
    {
        var delta = (value - fromMin) / (fromMax - fromMin);
        return toMin + Math.Clamp(delta, 0.0, 1.0) * (toMax - toMin);
    }

    private static double Map(double value, double fromMin, double fromMax, double toMin, double toMax) =>
        toMin + (value - fromMin) / (fromMax - fromMin) * (toMax - toMin);

    private static int GridX(int x) => x >> 4;

    private static int FromGridX(int gridX, int offset) => (gridX << 4) + offset;

    private static int GridY(int y) => FloorDiv(y, 12);

    // Equal to vanilla's floor(y / 12.0) for any Y a world can have.
    private static int FloorDiv(int value, int divisor) =>
        value >= 0 ? value / divisor : (value - divisor + 1) / divisor;

    private static int FromGridY(int gridY, int offset) => gridY * 12 + offset;

    private static int GridZ(int z) => z >> 4;

    private static int FromGridZ(int gridZ, int offset) => (gridZ << 4) + offset;

    /// <summary>
    /// An aquifer center and its index in the caches.
    /// </summary>
    private readonly record struct Candidate(int Index, int X, int Y, int Z);
}
