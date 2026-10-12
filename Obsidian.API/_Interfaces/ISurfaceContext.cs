namespace Obsidian.API;

/// <summary>
/// The block a surface rule is evaluated for, like vanilla's <c>SurfaceRules.Context</c>: surface building walks each
/// column of a chunk down from its surface and evaluates the noise settings' rule for every block of the default block
/// (usually stone) it finds.
/// </summary>
/// <remarks>
/// A context is reused for the next block, so don't keep it, or values read from it, past the call.
/// </remarks>
public interface ISurfaceContext
{
    public int BlockX { get; }

    public int BlockY { get; }

    public int BlockZ { get; }

    /// <summary>
    /// The biome at the block, like <c>minecraft:plains</c>.
    /// </summary>
    public string Biome { get; }

    /// <summary>
    /// The column's surface depth, from the surface noise: how deep surface blocks like dirt go (vanilla's 3 to 6).
    /// </summary>
    public int SurfaceDepth { get; }

    /// <summary>
    /// How many blocks of the default block are above this one, itself included, up to air or a fluid.
    /// </summary>
    public int StoneDepthAbove { get; }

    /// <summary>
    /// How many blocks of the default block are below this one, itself included, down to air or a fluid.
    /// </summary>
    public int StoneDepthBelow { get; }

    /// <summary>
    /// The Y above the top of the fluid over this block, or <see cref="int.MinValue"/> when there's none.
    /// </summary>
    public int WaterHeight { get; }

    /// <summary>
    /// Whether the terrain rises steeply around the column, as vanilla's <c>minecraft:steep</c> condition tests.
    /// </summary>
    public bool Steep { get; }

    /// <summary>
    /// The lowest Y above the noise's preliminary surface, as vanilla's <c>minecraft:above_preliminary_surface</c>
    /// condition tests.
    /// </summary>
    public int MinSurfaceLevel { get; }

    /// <summary>
    /// Evaluates another rule, such as one of a plugin rule's properties, for this block.
    /// </summary>
    /// <returns>The block it gives, or null when it doesn't apply.</returns>
    public IBlock? Apply(ISurfaceRule rule);

    /// <summary>
    /// Tests another condition, such as one of a plugin rule's properties, for this block.
    /// </summary>
    public bool Test(ISurfaceCondition condition);
}
