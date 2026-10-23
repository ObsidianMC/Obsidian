using Obsidian.API.Advancements;
using System.Collections.Frozen;

namespace Obsidian.Registries;

internal static partial class AdvancementsRegistry
{
    // A simple tree layout keeps every display reachable. Vanilla's compact tree spacing is deferred;
    // these are UI positions, rather than behavior, registry or save identities.
    private static FrozenDictionary<string, Advancement> PlaceDisplays(IEnumerable<Advancement> definitions)
    {
        var result = definitions.ToDictionary(definition => definition.Identifier);
        var rows = new Dictionary<string, int>();
        foreach (var definition in result.Values.OrderBy(value => value.Identifier, StringComparer.Ordinal).ToArray())
        {
            if (definition.Display is not AdvancementDisplay display)
                continue;

            var root = definition.Identifier;
            var depth = 0;
            var ancestors = new HashSet<string> { definition.Identifier };
            var parent = definition.Parent;
            while (result.TryGetValue(parent, out var ancestor) && ancestors.Add(parent))
            {
                if (ancestor.Display is not null)
                    depth++;

                root = ancestor.Identifier;
                parent = ancestor.Parent;
            }

            var row = rows.GetValueOrDefault(root);
            rows[root] = row + 1;
            var positioned = new AdvancementDisplay(display.Title, display.Description, display.Icon,
                display.AdvancementFrameType, display.Flags, display.BackgroundTexture, depth, row)
            {
                IconStack = display.IconStack,
                AnnounceToChat = display.AnnounceToChat
            };
            result[definition.Identifier] = new Advancement(definition.Identifier, definition.Parent,
                positioned, definition.Reward, definition.Criteria)
            {
                Requirements = definition.Requirements,
                SendsTelemetryEvent = definition.SendsTelemetryEvent
            };
        }

        return result.ToFrozenDictionary();
    }
}
