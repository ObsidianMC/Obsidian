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
    }

    public IWorldGenLevel Level => this.tree.Level;

    public IRandomSource Random => this.tree.Random;

    public WorldGenerationContext Generation => this.tree.Generation;

    // Sorted on first use: decorators only add decorations, so the tree's logs, leaves and roots don't change meanwhile.
    public IReadOnlyList<Vector> Logs => field ??= SortedByY(this.tree.Logs);

    public IReadOnlyList<Vector> Leaves => field ??= SortedByY(this.tree.Foliage);

    public IReadOnlyList<Vector> Roots => field ??= SortedByY(this.tree.Roots);

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

    // A stable sort by Y, like fastutil's ObjectArrayList.sort used by vanilla: the keys put the set's order after Y.
    private static Vector[] SortedByY(VanillaBlockPosSet positions)
    {
        var sorted = new Vector[positions.Count];
        var keys = new long[sorted.Length];
        var index = 0;
        foreach (var position in positions)
        {
            keys[index] = (long)position.Y << 32 | (uint)index;
            sorted[index++] = position;
        }

        Array.Sort(keys, sorted);
        return sorted;
    }
}
