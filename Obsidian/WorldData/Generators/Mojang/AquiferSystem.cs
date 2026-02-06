using Obsidian.API.World.Generator.DensityFunctions;
using Obsidian.API.World.Generator.Noise;
using Obsidian.Registries;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Responsible for determining fluid placement in caves and below sea level.
/// Single Responsibility: Aquifer fluid logic.
/// Based on Mojang's NoiseBasedAquifer implementation.
/// </summary>
internal class AquiferSystem
{
    private readonly NoiseSetting settings;
    private readonly IDensityFunction? barrierNoise;
    private readonly IDensityFunction? fluidLevelFloodednessNoise;
    private readonly IDensityFunction? fluidLevelSpreadNoise;
    private readonly IDensityFunction? lavaNoise;
    private readonly IDensityFunction? preliminarySurfaceLevel;

    public AquiferSystem(NoiseSetting settings)
    {
        this.settings = settings;
        this.barrierNoise = settings.NoiseRouter.Barrier;
        this.fluidLevelFloodednessNoise = settings.NoiseRouter.FluidLevelFloodedness;
        this.fluidLevelSpreadNoise = settings.NoiseRouter.FluidLevelSpread;
        this.lavaNoise = settings.NoiseRouter.Lava;
        this.preliminarySurfaceLevel = settings.NoiseRouter.PreliminarySurfaceLevel;
    }

    /// <summary>
    /// Determines if a block at the given position should be fluid based on aquifer logic.
    /// Returns null if the block should remain air (not part of an aquifer).
    /// This is only called for blocks where density <= 0 (air/cave spaces).
    /// </summary>
    public IBlock? GetFluidBlock(int worldX, int worldY, int worldZ, double preliminarySurfaceLevel)
    {
        // First check: above preliminary surface + margin, just use simple sea level check
        // This handles oceans and rivers without expensive aquifer calculations
        if (worldY > preliminarySurfaceLevel + 8)
        {
            // Simple ocean/river fill - below sea level = water, above = air
            return worldY < settings.SeaLevel ? BlocksRegistry.GetFromSimpleState(settings.DefaultFluid) : null;
        }

        // Below preliminary surface - use aquifer system for underground water
        if (!settings.AquifersEnabled)
            return null;

        // Compute the fluid surface level for this XZ position
        var fluidStatus = ComputeFluidStatus(worldX, worldY, worldZ, preliminarySurfaceLevel);

        // Only place fluid if this block Y is below the computed fluid level
        if (worldY >= fluidStatus.FluidLevel)
            return null;

        return fluidStatus.FluidType;
    }

    private FluidStatus ComputeFluidStatus(int x, int y, int z, double surfaceLevel)
    {
        int adjustedSurfaceLevel = (int)surfaceLevel + 8;

        // If the aquifer cell bottom is above surface, use global fluid (sea level)
        int bottomOfAquiferCell = y - 12;
        if (bottomOfAquiferCell > adjustedSurfaceLevel)
        {
            return new FluidStatus(int.MinValue, null);
        }

        // Compute floodedness at this location
        int distanceBelowSurface = adjustedSurfaceLevel - y;

        // Clamp floodedness factor based on depth below surface
        double floodednessFactor = distanceBelowSurface > 0
            ? Math.Clamp((double)distanceBelowSurface / 64.0, 0.0, 1.0)
            : 0.0;

        // Sample floodedness noise
        double floodednessNoise = fluidLevelFloodednessNoise?.GetValue(x, y, z) ?? 0.0;
        floodednessNoise = Math.Clamp(floodednessNoise, -1.0, 1.0);

        // Compute thresholds based on depth factor
        // Map floodednessFactor from [1.0, 0.0] to threshold ranges
        double fullyFloodedThreshold = Lerp(floodednessFactor, -0.3, 0.8);
        double partiallyFloodedThreshold = Lerp(floodednessFactor, -0.8, 0.4);

        double partiallyFloodedness = floodednessNoise - partiallyFloodedThreshold;
        double fullyFloodedness = floodednessNoise - fullyFloodedThreshold;

        int fluidSurfaceLevel;

        if (fullyFloodedness > 0.0)
        {
            // Fully flooded - use sea level
            fluidSurfaceLevel = settings.SeaLevel;
        }
        else if (partiallyFloodedness > 0.0)
        {
            // Partially flooded - compute randomized fluid level
            fluidSurfaceLevel = ComputeRandomizedFluidSurfaceLevel(x, y, z, (int)surfaceLevel);
        }
        else
        {
            // Not flooded
            return new FluidStatus(int.MinValue, null);
        }

        // Determine fluid type (water or lava)
        var fluidType = ComputeFluidType(x, y, z, fluidSurfaceLevel);

        return new FluidStatus(fluidSurfaceLevel, fluidType);
    }

    private int ComputeRandomizedFluidSurfaceLevel(int x, int y, int z, int lowestPreliminarySurface)
    {
        // Aquifer cells are 16x40x16
        int fluidLevelCellX = Math.DivRem(x, 16, out _);
        int fluidLevelCellY = Math.DivRem(y, 40, out _);
        int fluidLevelCellZ = Math.DivRem(z, 16, out _);

        // Get cell center Y
        int fluidCellMiddleY = fluidLevelCellY * 40 + 20;

        // Sample spread noise at cell center
        double fluidLevelSpread = fluidLevelSpreadNoise?.GetValue(
            fluidLevelCellX * 16 + 8,
            fluidCellMiddleY,
            fluidLevelCellZ * 16 + 8) ?? 0.0;

        // Apply spread (max ±10 blocks)
        int fluidLevelSpreadQuantized = Quantize(fluidLevelSpread * 10.0, 3);
        int targetFluidSurfaceLevel = fluidCellMiddleY + fluidLevelSpreadQuantized;

        // Clamp to surface level
        return Math.Min(lowestPreliminarySurface, targetFluidSurfaceLevel);
    }

    private IBlock? ComputeFluidType(int x, int y, int z, int fluidSurfaceLevel)
    {
        var defaultFluid = BlocksRegistry.GetFromSimpleState(settings.DefaultFluid);

        // Only check for lava in deep areas
        if (fluidSurfaceLevel > -10)
            return defaultFluid;

        // Lava cells are 64x40x64
        int fluidTypeCellX = Math.DivRem(x, 64, out _);
        int fluidTypeCellY = Math.DivRem(y, 40, out _);
        int fluidTypeCellZ = Math.DivRem(z, 64, out _);

        // Sample lava noise at cell center
        double lavaNoiseValue = lavaNoise?.GetValue(
            fluidTypeCellX * 64 + 32,
            fluidTypeCellY * 40 + 20,
            fluidTypeCellZ * 64 + 32) ?? 0.0;

        // Lava if noise absolute value > 0.3
        if (Math.Abs(lavaNoiseValue) > 0.3)
        {
            return BlocksRegistry.Lava;
        }

        return defaultFluid;
    }

    private static double Lerp(double t, double a, double b)
    {
        return a + t * (b - a);
    }

    private static int Quantize(double value, int divisor)
    {
        return (int)Math.Floor(value / divisor) * divisor;
    }

    private record struct FluidStatus(int FluidLevel, IBlock? FluidType);
}
