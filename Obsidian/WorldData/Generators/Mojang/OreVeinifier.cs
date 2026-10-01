namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Large copper (granite) and iron (tuff) ore veins carved into solid terrain during noise fill.
/// </summary>
internal static class OreVeinifier
{
    private const float VeininessThreshold = 0.4f;
    private const int EdgeRoundoffBegin = 20;
    private const double MaxEdgeRoundoff = 0.2;
    private const float VeinSolidness = 0.7f;
    private const float MinRichness = 0.1f;
    private const float MaxRichness = 0.3f;
    private const float MaxRichnessThreshold = 0.6f;
    private const float ChanceOfRawOreBlock = 0.02f;
    private const float SkipOreIfGapNoiseIsBelow = -0.3f;

    private static readonly VeinType Copper = new(BlocksRegistry.CopperOre, BlocksRegistry.RawCopperBlock, BlocksRegistry.Granite, 0, 50);
    private static readonly VeinType Iron = new(BlocksRegistry.DeepslateIronOre, BlocksRegistry.RawIronBlock, BlocksRegistry.Tuff, -60, -8);

    /// <summary>
    /// Gets the vein block for a solid position, or <c>null</c> to keep the default block.
    /// </summary>
    /// <remarks>
    /// The thresholds are floats compared against doubles on purpose; vanilla widens them the same way.
    /// </remarks>
    public static IBlock? Compute(NoiseChunk noiseChunk, int x, int y, int z)
    {
        var toggle = noiseChunk.VeinToggle.GetValue(x, y, z);
        var vein = toggle > 0.0 ? Copper : Iron;
        var veininess = Math.Abs(toggle);

        var distanceToTop = vein.MaxY - y;
        var distanceToBottom = y - vein.MinY;
        if (distanceToBottom < 0 || distanceToTop < 0)
            return null;

        var edgeDistance = Math.Min(distanceToTop, distanceToBottom);
        var edgeRoundoff = ClampedMap(edgeDistance, 0.0, EdgeRoundoffBegin, -MaxEdgeRoundoff, 0.0);
        if (veininess + edgeRoundoff < VeininessThreshold)
            return null;

        var random = noiseChunk.RandomState.OreRandom.At(x, y, z);
        if (random.NextFloat() > VeinSolidness || noiseChunk.VeinRidged.GetValue(x, y, z) >= 0.0)
            return null;

        var richness = ClampedMap(veininess, VeininessThreshold, MaxRichnessThreshold, MinRichness, MaxRichness);
        if (random.NextFloat() < richness && noiseChunk.VeinGap.GetValue(x, y, z) > SkipOreIfGapNoiseIsBelow)
            return random.NextFloat() < ChanceOfRawOreBlock ? vein.RawOreBlock : vein.Ore;

        return vein.Filler;
    }

    private static double ClampedMap(double value, double fromMin, double fromMax, double toMin, double toMax)
    {
        var delta = (value - fromMin) / (fromMax - fromMin);
        return delta < 0.0 ? toMin : delta > 1.0 ? toMax : toMin + delta * (toMax - toMin);
    }

    private sealed record VeinType(IBlock Ore, IBlock RawOreBlock, IBlock Filler, int MinY, int MaxY);
}
