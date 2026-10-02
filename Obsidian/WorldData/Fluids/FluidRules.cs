namespace Obsidian.WorldData.Fluids;

/// <summary>
/// The dimension setting and game rules vanilla's fluids read.
/// </summary>
/// <param name="FastLava">Vanilla's <c>fast_lava</c> environment attribute (<c>ultrawarm</c> before 1.21.11).</param>
/// <param name="WaterSourceConversion">The <c>water_source_conversion</c> game rule.</param>
/// <param name="LavaSourceConversion">The <c>lava_source_conversion</c> game rule.</param>
internal sealed record FluidRules(bool FastLava, bool WaterSourceConversion = true, bool LavaSourceConversion = false)
{
    /// <summary>
    /// Vanilla's defaults for a dimension type.
    /// </summary>
    /// <remarks>
    /// In 1.21.11 only <c>minecraft:the_nether</c> sets <c>fast_lava</c>; Obsidian's dimension codecs predate the attribute,
    /// so the nether is recognized by name, and a codec's legacy <c>ultrawarm</c> flag counts too.
    /// </remarks>
    public static FluidRules ForDimension(string dimensionType, bool ultrawarm = false) =>
        new(ultrawarm || dimensionType == "minecraft:the_nether");
}
