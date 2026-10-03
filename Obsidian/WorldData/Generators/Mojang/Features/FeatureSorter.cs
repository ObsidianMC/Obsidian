namespace Obsidian.WorldData.Generators.Mojang.Features;

/// <summary>
/// Orders every placed feature of every biome into one list per decoration step, like vanilla's FeatureSorter.
/// </summary>
/// <remarks>
/// Each biome's features (across all steps, in order) form a chain in a graph; a depth-first topological sort of that
/// graph gives the global order. A feature's position in its step list becomes part of its random seed, so this must
/// match vanilla exactly, including the order biomes are visited in.
/// </remarks>
internal static class FeatureSorter
{
    /// <param name="biomes">Biomes in vanilla's possibleBiomes order.</param>
    /// <param name="featuresOf">The decoration steps of a biome, each a list of placed features.</param>
    public static IReadOnlyList<StepFeatures> Build<TBiome>(IReadOnlyList<TBiome> biomes,
        Func<TBiome, IReadOnlyList<IReadOnlyList<PlacedFeature>>> featuresOf)
    {
        var featureIndices = new Dictionary<PlacedFeature, int>(ReferenceEqualityComparer.Instance);
        var graph = new SortedDictionary<FeatureNode, SortedSet<FeatureNode>>();
        var stepCount = 0;

        foreach (var biome in biomes)
        {
            var steps = featuresOf(biome);
            stepCount = Math.Max(stepCount, steps.Count);

            var chain = new List<FeatureNode>();
            for (var step = 0; step < steps.Count; step++)
            {
                foreach (var feature in steps[step])
                {
                    if (!featureIndices.TryGetValue(feature, out var index))
                    {
                        index = featureIndices.Count;
                        featureIndices[feature] = index;
                    }

                    chain.Add(new FeatureNode(index, step, feature));
                }
            }

            for (var i = 0; i < chain.Count; i++)
            {
                if (!graph.TryGetValue(chain[i], out var next))
                {
                    next = [];
                    graph[chain[i]] = next;
                }

                if (i < chain.Count - 1)
                    next.Add(chain[i + 1]);
            }
        }

        var visited = new HashSet<FeatureNode>();
        var inProgress = new HashSet<FeatureNode>();
        var sorted = new List<FeatureNode>();

        foreach (var node in graph.Keys)
        {
            if (!visited.Contains(node) && DepthFirstSearch(graph, visited, inProgress, sorted, node))
                throw new InvalidOperationException("Feature order cycle found.");
        }

        sorted.Reverse();

        var result = new StepFeatures[stepCount];
        for (var step = 0; step < stepCount; step++)
            result[step] = new StepFeatures(sorted.Where(node => node.Step == step).Select(node => node.Feature).ToArray());

        return result;
    }

    /// <summary>
    /// Vanilla's Graph.depthFirstSearch: post-order traversal that reports whether a cycle was found.
    /// </summary>
    private static bool DepthFirstSearch(SortedDictionary<FeatureNode, SortedSet<FeatureNode>> graph, HashSet<FeatureNode> visited,
        HashSet<FeatureNode> inProgress, List<FeatureNode> sorted, FeatureNode node)
    {
        if (visited.Contains(node))
            return false;

        if (!inProgress.Add(node))
            return true;

        if (graph.TryGetValue(node, out var next))
        {
            foreach (var child in next)
            {
                if (DepthFirstSearch(graph, visited, inProgress, sorted, child))
                    return true;
            }
        }

        inProgress.Remove(node);
        visited.Add(node);
        sorted.Add(node);
        return false;
    }

    /// <summary>
    /// A feature at a step; nodes are ordered and compared by step, then by the order features were first seen.
    /// </summary>
    private readonly record struct FeatureNode(int FeatureIndex, int Step, PlacedFeature Feature) : IComparable<FeatureNode>
    {
        public int CompareTo(FeatureNode other)
        {
            var step = this.Step.CompareTo(other.Step);
            return step != 0 ? step : this.FeatureIndex.CompareTo(other.FeatureIndex);
        }

        public bool Equals(FeatureNode other) => this.Step == other.Step && this.FeatureIndex == other.FeatureIndex;

        public override int GetHashCode() => HashCode.Combine(this.Step, this.FeatureIndex);
    }
}

/// <summary>
/// The globally ordered placed features of one decoration step.
/// </summary>
internal sealed class StepFeatures
{
    private readonly Dictionary<PlacedFeature, int> indices;

    public IReadOnlyList<PlacedFeature> Features { get; }

    public StepFeatures(PlacedFeature[] features)
    {
        this.Features = features;
        this.indices = new Dictionary<PlacedFeature, int>(ReferenceEqualityComparer.Instance);

        for (var i = 0; i < features.Length; i++)
            this.indices[features[i]] = i;
    }

    public int IndexOf(PlacedFeature feature) => this.indices[feature];
}
