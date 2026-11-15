using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.World.Features.Trees;

/// <summary>
/// Adds extras (vines, beehives, cocoa, ground cover...) to a placed tree, like vanilla's <c>TreeDecorator</c>.
/// Plugins add types with <c>RegisterWorldgenType&lt;TreeDecorator, T&gt;</c>.
/// </summary>
public abstract class TreeDecorator
{
    /// <summary>
    /// Gets the registered codec type identifier for this world generation component.
    /// </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Adds decorations to the placed tree using the supplied context.
    /// </summary>
    public abstract void Place(TreeDecoratorContext context);
}

/// <summary>
/// What a <see cref="TreeDecorator"/> sees of the placed tree, like vanilla's <c>TreeDecorator.Context</c>. Only the
/// server creates contexts.
/// </summary>
/// <remarks>
/// <see cref="Logs"/>, <see cref="Leaves"/> and <see cref="Roots"/> are the tree's positions in the order vanilla
/// iterates them, stable-sorted by Y (lowest first), exactly as vanilla builds them.
/// </remarks>
public abstract class TreeDecoratorContext
{
    internal TreeDecoratorContext()
    {
    }

    /// <summary>
    /// Gets the world generation view used to read and place blocks.
    /// </summary>
    public abstract IWorldGenLevel Level { get; }

    /// <summary>
    /// Gets the random source shared by this placement's operations.
    /// </summary>
    public abstract IRandomSource Random { get; }

    /// <summary>
    /// Gets the vertical generation bounds used to resolve height providers and anchors.
    /// </summary>
    public abstract WorldGenerationContext Generation { get; }

    /// <summary>
    /// Gets the tree's log positions in world blocks, ordered from lowest Y upward.
    /// </summary>
    public abstract IReadOnlyList<Vector> Logs { get; }

    /// <summary>
    /// Gets the tree's foliage positions in world blocks, ordered from lowest Y upward.
    /// </summary>
    public abstract IReadOnlyList<Vector> Leaves { get; }

    /// <summary>
    /// Gets the tree's root positions in world blocks, ordered from lowest Y upward.
    /// </summary>
    public abstract IReadOnlyList<Vector> Roots { get; }

    /// <summary>
    /// Places a decoration block; its position counts as part of the tree for the leaf distance and shape updates.
    /// </summary>
    public abstract void SetBlock(Vector position, IBlock block);

    /// <summary>
    /// Places a vine attached on <paramref name="face"/> (e.g. <see cref="BlockFace.East"/> sets <c>east=true</c>).
    /// </summary>
    public void PlaceVine(Vector position, BlockFace face) =>
        this.SetBlock(position, TreeBlockRules.WithProperty(TreeBlockRules.Vine, face.PropertyName(), true));

    /// <summary>
    /// Tests whether the world block position contains air.
    /// </summary>
    public bool IsAir(Vector position) => this.Level.GetBlock(position).IsAir;

    /// <summary>
    /// Vanilla <c>TreeFeature.getLowestTrunkOrRootOfTree</c>: the logs, the roots, or both when the lowest root and
    /// the lowest log share a Y level.
    /// </summary>
    public IReadOnlyList<Vector> GetLowestTrunkOrRoot()
    {
        if (this.Roots.Count == 0)
            return this.Logs;

        if (this.Logs.Count > 0 && this.Roots[0].Y == this.Logs[0].Y)
            return [.. this.Logs, .. this.Roots];

        return this.Roots;
    }

    /// <summary>Vanilla <c>Util.shuffle</c> (Fisher-Yates from the end).</summary>
    public static void Shuffle<T>(IList<T> list, IRandomSource random)
    {
        for (var i = list.Count; i > 1; i--)
        {
            var j = random.NextInt(i);
            (list[i - 1], list[j]) = (list[j], list[i - 1]);
        }
    }
}
