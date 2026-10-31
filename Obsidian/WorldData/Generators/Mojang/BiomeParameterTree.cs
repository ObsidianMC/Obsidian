using System.Threading;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Nearest-neighbor index over biome climate ranges, built and searched exactly like vanilla's Climate.RTree
/// so ties between equally close biomes resolve the same way.
/// </summary>
internal sealed class BiomeParameterTree<T>
{
    private const int Dimensions = 7;

    // Searches remembered per thread; a power of two, with room for the quarts around a chunk.
    private const int RememberedSearches = 4096;

    private readonly Node root;

    // Vanilla starts each search from the previous result on the same thread; it only matters for exact ties.
    private readonly ThreadLocal<SearchState> searches;

    // Tells this tree's search states apart. The states hold this token rather than the tree, so the state a thread
    // searched last doesn't keep a discarded tree (and every thread's states) alive.
    private readonly object identity = new();

    // The state of the tree this thread searched last, which spares most searches the slower ThreadLocal lookup.
    [ThreadStatic]
    private static SearchState? lastSearched;

    /// <param name="childrenPerNode">6 for the live legacy pipeline, 19 for 26.3.</param>
    public BiomeParameterTree(IReadOnlyList<(ParameterPoint Point, T Value)> values, int childrenPerNode = 6)
    {
        if (values.Count == 0)
            throw new ArgumentException("Need at least one value to build the search tree.", nameof(values));

        var leaves = values.Select(value => (Node)new Leaf(value.Point.ParameterSpace(), value.Value)).ToList();
        this.root = Build(leaves, childrenPerNode);
        this.searches = new ThreadLocal<SearchState>(() => new SearchState(this.identity));
    }

    /// <remarks>
    /// <para>
    /// The search finds the smallest distance to the target. It returns the previous result if that is at the
    /// smallest distance, and otherwise the first leaf at the smallest distance in the tree's order, whatever the
    /// previous result was. So each thread remembers, per target, the smallest distance and that first leaf (once
    /// a search has returned it), and only searches the tree when neither answers: the result is the same.
    /// </para>
    /// <para>
    /// Material rules look a biome up for every block, mostly in the few quarts around it.
    /// </para>
    /// </remarks>
    public T Search(TargetPoint target)
    {
        ReadOnlySpan<long> point = [target.Temperature, target.Humidity, target.Continentalness, target.Erosion, target.Depth, target.Weirdness, 0L];
        var state = lastSearched;
        if (state is null || state.Owner != this.identity)
            lastSearched = state = this.searches.Value!;

        var last = state.Last;

        // The previous result is at the smallest distance from its own target, so asking again returns it.
        if (last is not null && state.LastTarget == target)
            return last.Value;

        var slot = (int)(Hash(target) & (RememberedSearches - 1));
        ref var remembered = ref state.Remembered[slot];

        if (remembered.Leaf is not null && remembered.Target == target)
        {
            if (last is not null && last.Distance(point) == remembered.Distance)
            {
                state.LastTarget = target;
                return last.Value;
            }

            if (remembered.First is Leaf first)
            {
                state.Last = first;
                state.LastTarget = target;
                return first.Value;
            }
        }

        var leaf = this.root.Search(point, last);
        var distance = leaf.Distance(point);

        // A result other than the previous one is the first leaf at the smallest distance.
        var isFirst = !ReferenceEquals(leaf, last);
        remembered = new RememberedSearch(target, distance, leaf, isFirst ? leaf : null);
        state.Last = leaf;
        state.LastTarget = target;

        return leaf.Value;
    }

    /// <summary>
    /// What decides every search for <paramref name="target"/> (see <see cref="Search(TargetPoint)"/>'s remarks): the
    /// smallest distance and the first leaf at it. It doesn't depend on, or change, the thread's previous result.
    /// </summary>
    /// <param name="near">A target prepared nearby, whose leaf is likely close, or <c>default</c>.</param>
    public Prepared Prepare(TargetPoint target, in Prepared near = default)
    {
        var point = Point(target);

        // Starting from a close leaf prunes most of the tree. That search finds the smallest distance, but on a tie
        // returns the leaf it started from rather than the first one.
        var start = near.First as Leaf ?? this.State().Last;
        var nearest = this.root.Search(point, start);
        var distance = nearest.Distance(point);
        var first = this.root.FirstAt(point, distance)!;

        return new Prepared(target, distance, first);
    }

    /// <summary>
    /// Whether the thread's previous result is the first leaf of <paramref name="prepared"/>: then searching for it
    /// returns that leaf whatever the tie.
    /// </summary>
    public bool IsPrevious(in Prepared prepared) => ReferenceEquals(this.State().Last, prepared.First);

    /// <summary>
    /// Searches for a prepared target: the same result, and the same previous result for the thread's next search, as
    /// <see cref="Search(TargetPoint)"/> gives for its target.
    /// </summary>
    public T Search(in Prepared prepared)
    {
        var state = this.State();
        var target = prepared.Target;
        var first = (Leaf)prepared.First;
        var last = state.Last;
        state.LastTarget = target;

        if (ReferenceEquals(last, first))
            return first.Value;

        if (last is not null)
        {
            var point = Point(target);

            if (last.Distance(point) == prepared.Distance)
                return last.Value;
        }

        state.Last = first;
        return first.Value;
    }

    private SearchState State()
    {
        var state = lastSearched;
        if (state is null || state.Owner != this.identity)
            lastSearched = state = this.searches.Value!;

        return state;
    }

    // A target as the tree's 7 parameters; the last, the offset, is 0 for every target.
    private static PointBuffer Point(in TargetPoint target)
    {
        var point = new PointBuffer();
        point[0] = target.Temperature;
        point[1] = target.Humidity;
        point[2] = target.Continentalness;
        point[3] = target.Erosion;
        point[4] = target.Depth;
        point[5] = target.Weirdness;

        return point;
    }

    [System.Runtime.CompilerServices.InlineArray(Dimensions)]
    private struct PointBuffer
    {
        private long element;
    }

    // Cheaper than the record's hash, which combines each field through an equality comparer.
    private static uint Hash(in TargetPoint target)
    {
        var hash = target.Temperature * 31 + target.Humidity;
        hash = hash * 31 + target.Continentalness;
        hash = hash * 31 + target.Erosion;
        hash = hash * 31 + target.Depth;
        hash = hash * 31 + target.Weirdness;

        return unchecked((uint)(hash ^ hash >> 32) * 0x9E3779B1u);
    }

    private static Node Build(List<Node> children, int childrenPerNode)
    {
        if (children.Count == 1)
            return children[0];

        if (children.Count <= childrenPerNode)
        {
            // Enumerable.OrderBy is stable like Java's List.sort, which keeps the tree identical to vanilla's.
            var sorted = children.OrderBy(child => child.ParameterSpace.Sum(parameter => Math.Abs(parameter.Midpoint))).ToList();
            return new SubTree(sorted);
        }

        var bestCost = long.MaxValue;
        var bestAxis = -1;
        List<SubTree>? bestBuckets = null;

        for (var axis = 0; axis < Dimensions; axis++)
        {
            // Vanilla sorts the same list in place for every axis, so full ties keep the previous axis' order.
            children = Sort(children, axis, absolute: false);
            var buckets = Bucketize(children, childrenPerNode);
            var cost = buckets.Sum(bucket => Cost(bucket.ParameterSpace));

            if (bestCost > cost)
            {
                bestCost = cost;
                bestAxis = axis;
                bestBuckets = buckets;
            }
        }

        return new SubTree(Sort(bestBuckets!.Cast<Node>(), bestAxis, absolute: true)
            .Select(bucket => Build([.. ((SubTree)bucket).Children], childrenPerNode))
            .ToList());
    }

    private static List<Node> Sort(IEnumerable<Node> nodes, int axis, bool absolute)
    {
        var ordered = nodes.OrderBy(node => SortKey(node, axis, absolute));

        for (var offset = 1; offset < Dimensions; offset++)
        {
            var nextAxis = (axis + offset) % Dimensions;
            ordered = ordered.ThenBy(node => SortKey(node, nextAxis, absolute));
        }

        return ordered.ToList();
    }

    private static long SortKey(Node node, int axis, bool absolute)
    {
        var midpoint = node.ParameterSpace[axis].Midpoint;
        return absolute ? Math.Abs(midpoint) : midpoint;
    }

    private static List<SubTree> Bucketize(List<Node> nodes, int childrenPerNode)
    {
        var buckets = new List<SubTree>();
        var current = new List<Node>();
        var exponent = Math.Floor(Math.Log(nodes.Count - 0.01) / Math.Log(childrenPerNode));
        var bucketSize = (int)Math.Pow(childrenPerNode, exponent);

        foreach (var node in nodes)
        {
            current.Add(node);

            if (current.Count >= bucketSize)
            {
                buckets.Add(new SubTree(current));
                current = [];
            }
        }

        if (current.Count > 0)
            buckets.Add(new SubTree(current));

        return buckets;
    }

    private static long Cost(ClimateParameter[] parameterSpace) =>
        parameterSpace.Sum(parameter => Math.Abs(parameter.Max - parameter.Min));

    /// <summary>
    /// The squared distance from <paramref name="target"/> to a parameter space flattened to min, max pairs, summed like
    /// <see cref="ClimateParameter.Distance"/> per axis.
    /// </summary>
    private static long BoundsDistance(ReadOnlySpan<long> bounds, ReadOnlySpan<long> target)
    {
        bounds = bounds[..(Dimensions * 2)];
        target = target[..Dimensions];
        var distance = 0L;

        for (var i = 0; i < Dimensions; i++)
        {
            var above = target[i] - bounds[i * 2 + 1];
            var below = bounds[i * 2] - target[i];
            var axisDistance = above > 0L ? above : Math.Max(below, 0L);
            distance += axisDistance * axisDistance;
        }

        return distance;
    }

    private sealed class SearchState(object owner)
    {
        /// <summary>The identity of the tree this state belongs to.</summary>
        public object Owner { get; } = owner;

        public Leaf? Last { get; set; }

        /// <summary>The target <see cref="Last"/> was returned for.</summary>
        public TargetPoint LastTarget { get; set; }

        public RememberedSearch[] Remembered { get; } = new RememberedSearch[RememberedSearches];
    }

    /// <summary>A target with the smallest distance to it and the first leaf at that distance (see <see cref="Prepare"/>).</summary>
    public readonly struct Prepared
    {
        internal Prepared(TargetPoint target, long distance, object first)
        {
            this.Target = target;
            this.Distance = distance;
            this.First = first;
        }

        public TargetPoint Target { get; }

        internal long Distance { get; }

        // A Leaf, which is private to the tree.
        internal object First { get; }
    }

    /// <param name="Leaf">The leaf a search returned, which marks the entry as used.</param>
    /// <param name="Distance">The smallest distance from the target to any leaf.</param>
    /// <param name="First">The first leaf at that distance in the tree's order, once known.</param>
    private readonly record struct RememberedSearch(TargetPoint Target, long Distance, Leaf? Leaf, Leaf? First);

    private abstract class Node(ClimateParameter[] parameterSpace)
    {
        public ClimateParameter[] ParameterSpace { get; } = parameterSpace;

        /// <summary>
        /// <see cref="ParameterSpace"/> flattened to min, max pairs.
        /// </summary>
        public long[] Bounds { get; } = [.. parameterSpace.SelectMany(parameter => new[] { parameter.Min, parameter.Max })];

        public abstract Leaf Search(ReadOnlySpan<long> target, Leaf? candidate);

        /// <summary>
        /// The first leaf in the tree's order at <paramref name="distance"/>, the smallest distance to
        /// <paramref name="target"/>, or <c>null</c> when none is in this subtree.
        /// </summary>
        public abstract Leaf? FirstAt(ReadOnlySpan<long> target, long distance);

        public long Distance(ReadOnlySpan<long> target) => BoundsDistance(this.Bounds, target);
    }

    private sealed class Leaf(ClimateParameter[] parameterSpace, T value) : Node(parameterSpace)
    {
        public T Value { get; } = value;

        public override Leaf Search(ReadOnlySpan<long> target, Leaf? candidate) => this;

        public override Leaf? FirstAt(ReadOnlySpan<long> target, long distance) =>
            this.Distance(target) == distance ? this : null;
    }

    private sealed class SubTree(List<Node> children) : Node(BuildParameterSpace(children))
    {
        // The children's bounds back to back, since the search measures every child of the subtrees it visits.
        private readonly long[] childBounds = [.. children.SelectMany(child => child.Bounds)];

        public Node[] Children { get; } = [.. children];

        public override Leaf Search(ReadOnlySpan<long> target, Leaf? candidate)
        {
            var bestDistance = candidate is null ? long.MaxValue : candidate.Distance(target);
            var best = candidate;
            var children = this.Children;

            for (var i = 0; i < children.Length; i++)
            {
                var childDistance = BoundsDistance(this.childBounds.AsSpan(i * Dimensions * 2), target);
                if (bestDistance <= childDistance)
                    continue;

                // A leaf is its own result, at the distance just measured.
                var leaf = children[i] as Leaf;
                var leafDistance = childDistance;
                if (leaf is null)
                {
                    leaf = children[i].Search(target, best);
                    leafDistance = leaf.Distance(target);
                }

                if (bestDistance > leafDistance)
                {
                    bestDistance = leafDistance;
                    best = leaf;
                }
            }

            return best!;
        }

        // A search without a previous result returns the first leaf at the smallest distance: it only replaces its
        // best leaf with a strictly closer one, and only skips children that can't hold a closer one.
        public override Leaf? FirstAt(ReadOnlySpan<long> target, long distance)
        {
            var children = this.Children;

            for (var i = 0; i < children.Length; i++)
            {
                var childDistance = BoundsDistance(this.childBounds.AsSpan(i * Dimensions * 2), target);
                if (childDistance > distance)
                    continue;

                if (children[i].FirstAt(target, distance) is Leaf leaf)
                    return leaf;
            }

            return null;
        }

        private static ClimateParameter[] BuildParameterSpace(List<Node> children)
        {
            var space = (ClimateParameter[])children[0].ParameterSpace.Clone();

            foreach (var child in children.Skip(1))
            {
                for (var i = 0; i < Dimensions; i++)
                    space[i] = child.ParameterSpace[i].Span(space[i]);
            }

            return space;
        }
    }
}
