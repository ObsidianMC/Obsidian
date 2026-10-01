using System.Threading;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Nearest-neighbor index over biome climate ranges, built and searched exactly like vanilla's Climate.RTree
/// so ties between equally close biomes resolve the same way.
/// </summary>
internal sealed class BiomeParameterTree<T>
{
    private const int ChildrenPerNode = 6;
    private const int Dimensions = 7;

    private readonly Node root;

    // Vanilla starts each search from the previous result on the same thread; it only matters for exact ties.
    private readonly ThreadLocal<Leaf?> lastResult = new();

    public BiomeParameterTree(IReadOnlyList<(ParameterPoint Point, T Value)> values)
    {
        if (values.Count == 0)
            throw new ArgumentException("Need at least one value to build the search tree.", nameof(values));

        this.root = Build(values.Select(value => (Node)new Leaf(value.Point.ParameterSpace(), value.Value)).ToList());
    }

    public T Search(TargetPoint target)
    {
        var leaf = this.root.Search(target.ToParameterArray(), this.lastResult.Value);
        this.lastResult.Value = leaf;
        return leaf.Value;
    }

    private static Node Build(List<Node> children)
    {
        if (children.Count == 1)
            return children[0];

        if (children.Count <= ChildrenPerNode)
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
            var buckets = Bucketize(children);
            var cost = buckets.Sum(bucket => Cost(bucket.ParameterSpace));

            if (bestCost > cost)
            {
                bestCost = cost;
                bestAxis = axis;
                bestBuckets = buckets;
            }
        }

        return new SubTree(Sort(bestBuckets!.Cast<Node>(), bestAxis, absolute: true)
            .Select(bucket => Build([.. ((SubTree)bucket).Children]))
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

    private static List<SubTree> Bucketize(List<Node> nodes)
    {
        var buckets = new List<SubTree>();
        var current = new List<Node>();
        var bucketSize = (int)Math.Pow(6.0, Math.Floor(Math.Log(nodes.Count - 0.01) / Math.Log(6.0)));

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

    private abstract class Node(ClimateParameter[] parameterSpace)
    {
        public ClimateParameter[] ParameterSpace { get; } = parameterSpace;

        public abstract Leaf Search(long[] target, Leaf? candidate);

        public long Distance(long[] target)
        {
            var distance = 0L;

            for (var i = 0; i < Dimensions; i++)
            {
                var axisDistance = this.ParameterSpace[i].Distance(target[i]);
                distance += axisDistance * axisDistance;
            }

            return distance;
        }
    }

    private sealed class Leaf(ClimateParameter[] parameterSpace, T value) : Node(parameterSpace)
    {
        public T Value { get; } = value;

        public override Leaf Search(long[] target, Leaf? candidate) => this;
    }

    private sealed class SubTree(List<Node> children) : Node(BuildParameterSpace(children))
    {
        public Node[] Children { get; } = [.. children];

        public override Leaf Search(long[] target, Leaf? candidate)
        {
            var bestDistance = candidate is null ? long.MaxValue : candidate.Distance(target);
            var best = candidate;

            foreach (var child in this.Children)
            {
                var childDistance = child.Distance(target);
                if (bestDistance <= childDistance)
                    continue;

                var leaf = child.Search(target, best);
                var leafDistance = ReferenceEquals(child, leaf) ? childDistance : leaf.Distance(target);

                if (bestDistance > leafDistance)
                {
                    bestDistance = leafDistance;
                    best = leaf;
                }
            }

            return best!;
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
