using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.Tree;

/// <summary>
/// Adds extras (vines, beehives, cocoa, ground cover...) to a placed tree, like vanilla's <c>TreeDecorator</c>.
/// </summary>
public abstract class TreeDecorator
{
    public string Type { get; init; } = string.Empty;

    public abstract void Place(TreeDecoratorContext context);
}

/// <summary>
/// What a <see cref="TreeDecorator"/> sees of the placed tree, like vanilla's <c>TreeDecorator.Context</c>.
/// </summary>
/// <remarks>
/// <see cref="Logs"/>, <see cref="Leaves"/> and <see cref="Roots"/> are copies of the tree's position sets in Java
/// iteration order, stable-sorted by Y (lowest first), exactly as vanilla builds them.
/// </remarks>
public sealed class TreeDecoratorContext
{
    private static readonly IBlock vine = BlockStateProperties.GetState("minecraft:vine");

    private readonly TreeContext tree;

    internal TreeDecoratorContext(TreeContext tree)
    {
        this.tree = tree;
        this.Roots = SortedByY(tree.Roots);
        this.Logs = SortedByY(tree.Logs);
        this.Leaves = SortedByY(tree.Foliage);
    }

    public IWorldGenLevel Level => this.tree.Level;

    public IRandomSource Random => this.tree.Random;

    public WorldGenerationContext Generation => this.tree.Generation;

    public IReadOnlyList<Vector> Logs { get; }

    public IReadOnlyList<Vector> Leaves { get; }

    public IReadOnlyList<Vector> Roots { get; }

    /// <summary>Places a decoration block; positions are tracked so the leaf/shape update pass treats them as part of the tree.</summary>
    public void SetBlock(Vector position, IBlock block) => this.tree.SetDecoration(position, block);

    /// <summary>Places a vine attached on <paramref name="face"/> (e.g. <see cref="BlockFace.East"/> sets <c>east=true</c>).</summary>
    public void PlaceVine(Vector position, BlockFace face) => this.SetBlock(position, vine.WithProperty(face.PropertyName(), true));

    public bool IsAir(Vector position) => this.Level.GetBlock(position).IsAir;

    /// <summary>
    /// Vanilla <c>TreeFeature.getLowestTrunkOrRootOfTree</c>: the logs, the roots, or both when the lowest root and the
    /// lowest log share a Y level.
    /// </summary>
    public List<Vector> GetLowestTrunkOrRoot()
    {
        if (this.Roots.Count == 0)
            return [.. this.Logs];

        if (this.Logs.Count > 0 && this.Roots[0].Y == this.Logs[0].Y)
            return [.. this.Logs, .. this.Roots];

        return [.. this.Roots];
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

    // LINQ OrderBy is a stable sort, like fastutil's ObjectArrayList.sort used by vanilla.
    private static Vector[] SortedByY(VanillaBlockPosSet positions) => [.. positions.OrderBy(position => position.Y)];
}
