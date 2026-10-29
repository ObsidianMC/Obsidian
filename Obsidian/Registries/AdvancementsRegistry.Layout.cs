using Obsidian.API.Advancements;
using System.Collections.Frozen;

namespace Obsidian.Registries;

internal static partial class AdvancementsRegistry
{
    // Vanilla's server uses a tidy tree: unit separation at every depth, centered parents, and evenly distributed
    // shifts between conflicting sibling branches. Contours keep this independent of vanilla's threaded tree.
    internal static FrozenDictionary<string, Advancement> PlaceDisplays(IEnumerable<Advancement> definitions)
    {
        var result = definitions.ToDictionary(definition => definition.Identifier);
        var children = result.Values.ToLookup(definition => definition.Parent);
        var visited = new HashSet<string>();
        foreach (var root in children[""])
        {
            if (root.Display is null)
                continue;
            var branch = Build(root).Single();
            Arrange(branch);
            var positioned = Positions(branch, 0, 0).ToArray();
            var minimum = Math.Min(0, positioned.Min(entry => entry.Y));
            foreach (var (id, x, y) in positioned)
            {
                var definition = result[id];
                var display = definition.Display!;
                var placed = new AdvancementDisplay(display.Title, display.Description, display.Icon,
                    display.AdvancementFrameType, display.Flags, display.BackgroundTexture, x, y - minimum)
                {
                    IconStack = display.IconStack,
                    AnnounceToChat = display.AnnounceToChat
                };
                result[id] = new Advancement(id, definition.Parent, placed, definition.Reward, definition.Criteria)
                {
                    Requirements = definition.Requirements,
                    SendsTelemetryEvent = definition.SendsTelemetryEvent
                };
            }
        }
        return result.ToFrozenDictionary();

        IEnumerable<DisplayBranch> Build(Advancement definition)
        {
            if (!visited.Add(definition.Identifier))
                yield break;
            var descendants = children[definition.Identifier].SelectMany(Build).ToArray();
            if (definition.Display is not null)
                yield return new DisplayBranch(definition.Identifier, descendants);
            else
                foreach (var child in descendants)
                    yield return child;
        }
    }

    private sealed class DisplayBranch(string identifier, DisplayBranch[] children)
    {
        internal string Identifier { get; } = identifier;
        internal DisplayBranch[] Children { get; } = children;
        internal float Row;
        internal float Offset;
        internal void Move(float distance)
        {
            this.Row += distance;
            this.Offset += distance;
        }
    }

    private static void Arrange(DisplayBranch tree)
    {
        var branches = tree.Children;
        var slopes = new float[branches.Length];
        var shifts = new float[branches.Length];
        var boundary = new Dictionary<int, (DisplayBranch Node, float Offset, int Branch)>();
        for (var i = 0; i < branches.Length; i++)
        {
            var branch = branches[i];
            Arrange(branch);
            if (i > 0)
            {
                var center = branch.Row;
                branch.Row = branches[i - 1].Row + 1;
                branch.Offset = branch.Row - center;
            }

            var height = Contour(branch, left: true).Count;
            for (var depth = 1; depth < height; depth++)
            {
                if (!boundary.TryGetValue(depth, out var previous))
                    break;
                var current = Contour(branch, left: true)[depth];
                // Keep float operations in the server layout's order; these values are transmitted as floats.
                var distance = previous.Node.Row + previous.Offset - current.Node.Row - current.Offset + 1;
                if (distance <= 0)
                    continue;
                var slope = distance / (i - previous.Branch);
                slopes[i] -= slope;
                slopes[previous.Branch] += slope;
                shifts[i] += distance;
                branch.Move(distance);
            }

            foreach (var (depth, edge) in Contour(branch, left: false))
                boundary[depth] = (edge.Node, edge.Offset, i);
        }

        float displacement = 0, rate = 0;
        for (var i = branches.Length - 1; i >= 0; i--)
        {
            branches[i].Move(displacement);
            rate += slopes[i];
            displacement += shifts[i] + rate;
        }
        if (branches.Length > 0)
            tree.Row = (branches[0].Row + branches[^1].Row) / 2;
    }

    private static Dictionary<int, (DisplayBranch Node, float Offset)> Contour(DisplayBranch branch, bool left)
    {
        var result = new Dictionary<int, (DisplayBranch, float)>();
        Visit(branch, 0, 0);
        return result;

        void Visit(DisplayBranch node, int depth, float offset)
        {
            if (left)
                result.TryAdd(depth, (node, offset));
            else
                result[depth] = (node, offset);
            foreach (var child in node.Children)
                Visit(child, depth + 1, offset + node.Offset);
        }
    }

    private static IEnumerable<(string Id, int X, float Y)> Positions(DisplayBranch branch, int depth, float offset)
    {
        yield return (branch.Identifier, depth, branch.Row + offset);
        foreach (var child in branch.Children)
            foreach (var position in Positions(child, depth + 1, offset + branch.Offset))
                yield return position;
    }
}
