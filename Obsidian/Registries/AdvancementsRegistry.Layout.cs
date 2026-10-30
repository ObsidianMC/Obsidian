using Obsidian.API.Advancements;
using System.Collections.Frozen;

namespace Obsidian.Registries;

internal static partial class AdvancementsRegistry
{
    // Vanilla's server lays each tree out with Buchheim, Jünger and Leipert's linear-time version of Walker's tidy
    // tree: unit separation at every depth, centered parents, and evenly distributed shifts between conflicting
    // sibling branches. Its coordinates are floats, so the layout below keeps vanilla's threaded contours and the
    // order of every float operation; summing the same offsets in another order can round a position one ulp apart.
    internal static FrozenDictionary<string, Advancement> PlaceDisplays(IEnumerable<Advancement> definitions)
    {
        var result = definitions.ToDictionary(definition => definition.Identifier);
        var children = result.Values.ToLookup(definition => definition.Parent);
        var visited = new HashSet<string>();
        foreach (var root in children[""])
        {
            if (root.Display is null)
                continue;

            visited.Add(root.Identifier);
            var tree = new LayoutNode(root.Identifier, null, null, 1);
            AddChildren(tree, root.Identifier);
            FirstWalk(tree);
            var minimum = SecondWalk(tree, 0, 0, tree.Y);
            if (minimum < 0)
            {
                ThirdWalk(tree, -minimum);
            }

            Place(tree);
        }
        return result.ToFrozenDictionary();

        // A definition without a display takes no space; its descendants join its nearest displayed ancestor's
        // children in its place.
        void AddChildren(LayoutNode node, string parent)
        {
            foreach (var child in children[parent])
            {
                if (!visited.Add(child.Identifier))
                    continue;

                if (child.Display is null)
                {
                    AddChildren(node, child.Identifier);
                    continue;
                }

                var previous = node.Children.Count > 0 ? node.Children[^1] : null;
                var branch = new LayoutNode(child.Identifier, node, previous, node.Children.Count + 1);
                node.Children.Add(branch);
                AddChildren(branch, child.Identifier);
            }
        }

        void Place(LayoutNode node)
        {
            var definition = result[node.Identifier];
            var display = definition.Display!;
            var placed = new AdvancementDisplay(display.Title, display.Description, display.Icon,
                display.AdvancementFrameType, display.Flags, display.BackgroundTexture, node.Depth, node.Y)
            {
                IconStack = display.IconStack,
                AnnounceToChat = display.AnnounceToChat
            };
            result[node.Identifier] = new Advancement(node.Identifier, definition.Parent, placed, definition.Reward,
                definition.Criteria)
            {
                Requirements = definition.Requirements,
                SendsTelemetryEvent = definition.SendsTelemetryEvent
            };
            foreach (var child in node.Children)
                Place(child);
        }
    }

    private sealed class LayoutNode(string identifier, LayoutNode? parent, LayoutNode? previous, int number)
    {
        internal string Identifier { get; } = identifier;
        internal LayoutNode? Parent { get; } = parent;
        internal LayoutNode? Previous { get; } = previous;

        /// <summary>The node's 1-based position among its parent's children.</summary>
        internal int Number { get; } = number;
        internal List<LayoutNode> Children { get; } = [];

        internal int Depth;
        internal float Y = -1;
        internal float Mod;
        internal float Change;
        internal float Shift;
        internal LayoutNode? Thread;
        internal LayoutNode? Ancestor;

        /// <summary>The next node down this subtree's left contour.</summary>
        internal LayoutNode? LeftContour => this.Thread ?? (this.Children.Count > 0 ? this.Children[0] : null);

        /// <summary>The next node down this subtree's right contour.</summary>
        internal LayoutNode? RightContour => this.Thread ?? (this.Children.Count > 0 ? this.Children[^1] : null);
    }

    private static void FirstWalk(LayoutNode node)
    {
        node.Ancestor = node;
        if (node.Children.Count == 0)
        {
            node.Y = node.Previous is null ? 0 : node.Previous.Y + 1;
            return;
        }

        LayoutNode? defaultAncestor = null;
        foreach (var child in node.Children)
        {
            FirstWalk(child);
            defaultAncestor = Apportion(child, defaultAncestor ?? child);
        }

        ExecuteShifts(node);
        var midpoint = (node.Children[0].Y + node.Children[^1].Y) / 2;
        if (node.Previous is null)
        {
            node.Y = midpoint;
            return;
        }

        node.Y = node.Previous.Y + 1;
        node.Mod = node.Y - midpoint;
    }

    // Walks the right contour of the siblings left of node down against node's left contour, moving node right
    // wherever they're closer than one apart, then threads the shallower contour onto the deeper one.
    private static LayoutNode Apportion(LayoutNode node, LayoutNode defaultAncestor)
    {
        if (node.Previous is null)
            return defaultAncestor;

        var insideRight = node;
        var outsideRight = node;
        var insideLeft = node.Previous;
        var outsideLeft = node.Parent!.Children[0];
        var insideRightSum = insideRight.Mod;
        var outsideRightSum = outsideRight.Mod;
        var insideLeftSum = insideLeft.Mod;
        var outsideLeftSum = outsideLeft.Mod;
        while (insideLeft.RightContour is { } nextLeft && insideRight.LeftContour is { } nextRight)
        {
            insideLeft = nextLeft;
            insideRight = nextRight;
            outsideLeft = outsideLeft.LeftContour!;
            outsideRight = outsideRight.RightContour!;
            outsideRight.Ancestor = node;

            // Vanilla adds each side's offset sum before subtracting; subtracting term by term rounds differently.
            var shift = (insideLeft.Y + insideLeftSum) - (insideRight.Y + insideRightSum) + 1;
            if (shift > 0)
            {
                var ancestor = insideLeft.Ancestor!.Parent == node.Parent ? insideLeft.Ancestor : defaultAncestor;
                MoveSubtree(ancestor, node, shift);
                insideRightSum += shift;
                outsideRightSum += shift;
            }

            insideLeftSum += insideLeft.Mod;
            insideRightSum += insideRight.Mod;
            outsideLeftSum += outsideLeft.Mod;
            outsideRightSum += outsideRight.Mod;
        }

        if (insideLeft.RightContour is { } leftThread && outsideRight.RightContour is null)
        {
            outsideRight.Thread = leftThread;
            outsideRight.Mod += insideLeftSum - outsideRightSum;
            return defaultAncestor;
        }

        if (insideRight.LeftContour is { } rightThread && outsideLeft.LeftContour is null)
        {
            outsideLeft.Thread = rightThread;
            outsideLeft.Mod += insideRightSum - outsideLeftSum;
        }
        return node;
    }

    private static void MoveSubtree(LayoutNode left, LayoutNode right, float shift)
    {
        float subtrees = right.Number - left.Number;
        if (subtrees != 0)
        {
            right.Change -= shift / subtrees;
            left.Change += shift / subtrees;
        }

        right.Shift += shift;
        right.Y += shift;
        right.Mod += shift;
    }

    // Spreads the shifts Apportion recorded across the siblings between each moved pair, right to left.
    private static void ExecuteShifts(LayoutNode node)
    {
        float shift = 0;
        float change = 0;
        for (var i = node.Children.Count - 1; i >= 0; i--)
        {
            var child = node.Children[i];
            child.Y += shift;
            child.Mod += shift;
            change += child.Change;
            shift += child.Shift + change;
        }
    }

    private static float SecondWalk(LayoutNode node, float offset, int depth, float minimum)
    {
        node.Y += offset;
        node.Depth = depth;
        if (node.Y < minimum)
        {
            minimum = node.Y;
        }

        foreach (var child in node.Children)
            minimum = SecondWalk(child, offset + node.Mod, depth + 1, minimum);
        return minimum;
    }

    private static void ThirdWalk(LayoutNode node, float offset)
    {
        node.Y += offset;
        foreach (var child in node.Children)
            ThirdWalk(child, offset);
    }
}
