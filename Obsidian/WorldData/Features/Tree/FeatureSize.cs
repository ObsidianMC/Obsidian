namespace Obsidian.WorldData.Features.Tree;

/// <summary>
/// How much free space a tree needs around its trunk at each height, like vanilla's <c>FeatureSize</c>.
/// </summary>
public abstract class FeatureSize
{
    /// <summary>Marks <see cref="MinClippedHeight"/> as absent (vanilla's empty <c>OptionalInt</c>).</summary>
    public const int NoMinClippedHeight = -1;

    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// If set, a tree whose free height is below its full height may still grow, clipped, as long as it has at least
    /// this much room. <see cref="NoMinClippedHeight"/> when the JSON omits <c>min_clipped_height</c>.
    /// </summary>
    public int MinClippedHeight { get; init; } = NoMinClippedHeight;

    /// <summary>Radius that must be free at <paramref name="y"/> blocks above the trunk base of a tree of <paramref name="height"/>.</summary>
    public abstract int GetSizeAtHeight(int height, int y);
}
