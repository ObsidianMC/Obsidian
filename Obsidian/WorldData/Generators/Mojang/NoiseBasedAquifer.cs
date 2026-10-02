namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Underground lakes and lava pockets with their own fluid levels, separated by barriers.
/// </summary>
/// <remarks>
/// Mirrors vanilla's NoiseBasedAquifer. Aquifer centers sit on a jittered 16x12x16 grid; each position blends the
/// fluid status of its nearest centers. Instances cache per chunk and aren't thread-safe.
/// </remarks>
internal sealed class NoiseBasedAquifer : IAquifer
{
    private const int XRange = 10;
    private const int YRange = 9;
    private const int ZRange = 10;
    private const int WayBelowMinY = -32512;

    private static readonly double flowingUpdateSimilarity = Similarity(10 * 10, 12 * 12);

    // Chunk offsets, in chunks, of the preliminary surface samples around an aquifer center.
    private static readonly (int X, int Z)[] surfaceSamplingOffsets =
    [
        (0, 0), (-2, -1), (-1, -1), (0, -1), (1, -1), (-3, 0), (-2, 0), (-1, 0), (1, 0), (-2, 1), (-1, 1), (0, 1), (1, 1)
    ];

    private readonly NoiseChunk noiseChunk;
    private readonly FluidPicker globalFluidPicker;
    private readonly FluidStatus?[] aquiferCache;
    private readonly (int X, int Y, int Z)?[] aquiferLocationCache;
    private readonly int skipSamplingAboveY;
    private readonly int minGridX;
    private readonly int minGridY;
    private readonly int minGridZ;
    private readonly int gridSizeX;
    private readonly int gridSizeZ;

    // See GetCandidates.
    private readonly Candidate[] candidates = new Candidate[12];
    private int candidatesGridX = int.MinValue;
    private int candidatesGridY;
    private int candidatesGridZ;

    public bool ShouldScheduleFluidUpdate { get; private set; }

    public void ResetFluidUpdate() => this.ShouldScheduleFluidUpdate = false;

    public int GlobalFluidAboveY => this.skipSamplingAboveY;

    public NoiseBasedAquifer(NoiseChunk noiseChunk, int chunkX, int chunkZ, FluidPicker globalFluidPicker)
    {
        this.noiseChunk = noiseChunk;
        this.globalFluidPicker = globalFluidPicker;

        var chunkMinX = chunkX << 4;
        var chunkMinZ = chunkZ << 4;

        this.minGridX = GridX(chunkMinX - 5);
        var maxGridX = GridX(chunkMinX + 15 - 5) + 1;
        this.gridSizeX = maxGridX - this.minGridX + 1;

        this.minGridY = GridY(noiseChunk.MinY + 1) - 1;
        var maxGridY = GridY(noiseChunk.MinY + noiseChunk.Height + 1) + 1;
        var gridSizeY = maxGridY - this.minGridY + 1;

        this.minGridZ = GridZ(chunkMinZ - 5);
        var maxGridZ = GridZ(chunkMinZ + 15 - 5) + 1;
        this.gridSizeZ = maxGridZ - this.minGridZ + 1;

        var size = this.gridSizeX * gridSizeY * this.gridSizeZ;
        this.aquiferCache = new FluidStatus?[size];
        this.aquiferLocationCache = new (int, int, int)?[size];

        var maxSurfaceLevel = AdjustSurfaceLevel(noiseChunk.MaxPreliminarySurfaceLevel(
            FromGridX(this.minGridX, 0), FromGridZ(this.minGridZ, 0), FromGridX(maxGridX, 9), FromGridZ(maxGridZ, 9)));
        this.skipSamplingAboveY = FromGridY(GridY(maxSurfaceLevel + 12) + 1, 11) - 1;
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

        if (globalFluid.MaterialAt(y) == Material.Lava)
        {
            this.ShouldScheduleFluidUpdate = false;
            return BlocksRegistry.Lava;
        }

        var gridX = GridX(x - 5);
        var gridY = GridY(y + 1);
        var gridZ = GridZ(z - 5);

        // The four closest aquifer centers, nearest first. Ties move existing entries down, like vanilla's >= comparisons.
        int distance1 = int.MaxValue, distance2 = int.MaxValue, distance3 = int.MaxValue, distance4 = int.MaxValue;
        int index1 = 0, index2 = 0, index3 = 0, index4 = 0;

        foreach (var candidate in this.GetCandidates(gridX, gridY, gridZ))
        {
            var index = candidate.Index;
            var dx = candidate.X - x;
            var dy = candidate.Y - y;
            var dz = candidate.Z - z;
            var distance = dx * dx + dy * dy + dz * dz;

            if (distance1 >= distance)
            {
                (index4, index3, index2, index1) = (index3, index2, index1, index);
                (distance4, distance3, distance2, distance1) = (distance3, distance2, distance1, distance);
            }
            else if (distance2 >= distance)
            {
                (index4, index3, index2) = (index3, index2, index);
                (distance4, distance3, distance2) = (distance3, distance2, distance);
            }
            else if (distance3 >= distance)
            {
                (index4, index3) = (index3, index);
                (distance4, distance3) = (distance3, distance);
            }
            else if (distance4 >= distance)
            {
                index4 = index;
                distance4 = distance;
            }
        }

        var closest = this.GetAquiferStatus(index1);
        var similarity12 = Similarity(distance1, distance2);
        var block = closest.At(y);

        if (similarity12 <= 0.0)
        {
            this.ShouldScheduleFluidUpdate = similarity12 >= flowingUpdateSimilarity && closest != this.GetAquiferStatus(index2);
            return block;
        }

        if (closest.MaterialAt(y) == Material.Water && this.globalFluidPicker(x, y - 1, z).MaterialAt(y - 1) == Material.Lava)
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

        if (similarity13 > 0.0 && density + similarity12 * similarity13 * this.CalculatePressure(x, y, z, ref barrier, closest, third) > 0.0)
        {
            this.ShouldScheduleFluidUpdate = false;
            return null;
        }

        var similarity23 = Similarity(distance2, distance3);

        if (similarity23 > 0.0 && density + similarity12 * similarity23 * this.CalculatePressure(x, y, z, ref barrier, second, third) > 0.0)
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

    private static double Similarity(int firstDistance, int secondDistance) => 1.0 - (secondDistance - firstDistance) / 25.0;

    private double CalculatePressure(int x, int y, int z, ref double barrier, FluidStatus first, FluidStatus second)
    {
        var firstBlock = first.MaterialAt(y);
        var secondBlock = second.MaterialAt(y);

        if ((firstBlock == Material.Lava && secondBlock == Material.Water) || (firstBlock == Material.Water && secondBlock == Material.Lava))
            return 2.0;

        var levelDifference = Math.Abs(first.FluidLevel - second.FluidLevel);
        if (levelDifference == 0)
            return 0.0;

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
                barrier = this.noiseChunk.Barrier.GetValue(x, y, z);

            barrierValue = barrier;
        }

        return 2.0 * (barrierValue + pressure);
    }

    /// <summary>
    /// The 12 aquifer centers around grid cell (<paramref name="gridX"/>, <paramref name="gridY"/>, <paramref name="gridZ"/>)
    /// in search order. Neighboring blocks almost always share a cell, so the last cell's centers are kept.
    /// </summary>
    private ReadOnlySpan<Candidate> GetCandidates(int gridX, int gridY, int gridZ)
    {
        if (gridX == this.candidatesGridX && gridY == this.candidatesGridY && gridZ == this.candidatesGridZ)
            return this.candidates;

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
        if (this.aquiferLocationCache[index] is { } cached)
            return cached;

        var random = this.noiseChunk.RandomState.AquiferRandom.At(gridX, gridY, gridZ);
        var location = (FromGridX(gridX, random.NextInt(XRange)), FromGridY(gridY, random.NextInt(YRange)), FromGridZ(gridZ, random.NextInt(ZRange)));

        this.aquiferLocationCache[index] = location;
        return location;
    }

    private FluidStatus GetAquiferStatus(int index)
    {
        if (this.aquiferCache[index] is { } cached)
            return cached;

        var (x, y, z) = this.aquiferLocationCache[index]!.Value;
        var status = this.ComputeFluid(x, y, z);

        this.aquiferCache[index] = status;
        return status;
    }

    private FluidStatus ComputeFluid(int x, int y, int z)
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
            var preliminarySurface = this.noiseChunk.PreliminarySurfaceLevel(sampleX, sampleZ);
            var surface = AdjustSurfaceLevel(preliminarySurface);
            var isCenter = offsetX == 0 && offsetZ == 0;

            if (isCenter && bottom > surface)
                return globalFluid;

            var surfaceBelowTop = top > surface;
            if (surfaceBelowTop || isCenter)
            {
                var surfaceFluid = this.globalFluidPicker(sampleX, surface, sampleZ);
                if (!surfaceFluid.At(surface).IsAir)
                {
                    if (isCenter)
                        surfaceIsFlooded = true;

                    if (surfaceBelowTop)
                        return surfaceFluid;
                }
            }

            lowestPreliminarySurface = Math.Min(lowestPreliminarySurface, preliminarySurface);
        }

        var fluidLevel = this.ComputeSurfaceLevel(x, y, z, globalFluid, lowestPreliminarySurface, surfaceIsFlooded);
        return new FluidStatus(fluidLevel, this.ComputeFluidType(x, y, z, globalFluid, fluidLevel));
    }

    private static int AdjustSurfaceLevel(int level) => level + 8;

    private int ComputeSurfaceLevel(int x, int y, int z, FluidStatus globalFluid, int lowestPreliminarySurface, bool surfaceIsFlooded)
    {
        double partiallyFlooded;
        double fullyFlooded;

        // Deep dark regions stay dry.
        if (this.noiseChunk.Erosion.GetValue(x, y, z) < -0.225f && this.noiseChunk.Depth.GetValue(x, y, z) > 0.9f)
        {
            partiallyFlooded = -1.0;
            fullyFlooded = -1.0;
        }
        else
        {
            var distanceBelowSurface = lowestPreliminarySurface + 8 - y;
            var surfaceProximity = surfaceIsFlooded ? ClampedMap(distanceBelowSurface, 0.0, 64.0, 1.0, 0.0) : 0.0;
            var floodedness = Math.Clamp(this.noiseChunk.FluidLevelFloodedness.GetValue(x, y, z), -1.0, 1.0);
            var fullyFloodedThreshold = Map(surfaceProximity, 1.0, 0.0, -0.3, 0.8);
            var partiallyFloodedThreshold = Map(surfaceProximity, 1.0, 0.0, -0.8, 0.4);

            partiallyFlooded = floodedness - partiallyFloodedThreshold;
            fullyFlooded = floodedness - fullyFloodedThreshold;
        }

        if (fullyFlooded > 0.0)
            return globalFluid.FluidLevel;

        if (partiallyFlooded > 0.0)
            return this.ComputeRandomizedFluidSurfaceLevel(x, y, z, lowestPreliminarySurface);

        return WayBelowMinY;
    }

    private int ComputeRandomizedFluidSurfaceLevel(int x, int y, int z, int lowestPreliminarySurface)
    {
        var cellX = (int)Math.Floor(x / 16.0);
        var cellY = (int)Math.Floor(y / 40.0);
        var cellZ = (int)Math.Floor(z / 16.0);
        var cellMiddleY = cellY * 40 + 20;

        // Vanilla samples the spread noise at the cell indices, not at block coordinates.
        var spread = this.noiseChunk.FluidLevelSpread.GetValue(cellX, cellY, cellZ) * 10.0;
        var quantizedSpread = (int)Math.Floor(spread / 3) * 3;

        return Math.Min(lowestPreliminarySurface, cellMiddleY + quantizedSpread);
    }

    private IBlock ComputeFluidType(int x, int y, int z, FluidStatus globalFluid, int fluidLevel)
    {
        if (fluidLevel > -10 || fluidLevel == WayBelowMinY || globalFluid.FluidType.Material == Material.Lava)
            return globalFluid.FluidType;

        // Like the spread noise, the lava noise is sampled at cell indices.
        var lava = this.noiseChunk.Lava.GetValue((int)Math.Floor(x / 64.0), (int)Math.Floor(y / 40.0), (int)Math.Floor(z / 64.0));
        return Math.Abs(lava) > 0.3 ? BlocksRegistry.Lava : globalFluid.FluidType;
    }

    private static double ClampedMap(double value, double fromMin, double fromMax, double toMin, double toMax)
    {
        var delta = (value - fromMin) / (fromMax - fromMin);
        return delta < 0.0 ? toMin : delta > 1.0 ? toMax : toMin + delta * (toMax - toMin);
    }

    private static double Map(double value, double fromMin, double fromMax, double toMin, double toMax) =>
        toMin + (value - fromMin) / (fromMax - fromMin) * (toMax - toMin);

    private static int GridX(int x) => x >> 4;

    private static int FromGridX(int gridX, int offset) => (gridX << 4) + offset;

    private static int GridY(int y) => (int)Math.Floor(y / 12.0);

    private static int FromGridY(int gridY, int offset) => gridY * 12 + offset;

    private static int GridZ(int z) => z >> 4;

    private static int FromGridZ(int gridZ, int offset) => (gridZ << 4) + offset;

    /// <summary>
    /// An aquifer center and its index in the caches.
    /// </summary>
    private readonly record struct Candidate(int Index, int X, int Y, int Z);
}
