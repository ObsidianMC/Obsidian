using Obsidian.SourceGenerators.Registry.Models;

namespace Obsidian.SourceGenerators.Registry;

public partial class BlocksGenerator
{
    // The properties Obsidian.Registries.StateProperties has typed handles for. Only these are emitted, to keep the
    // table (and the static initializer that builds it) small.
    private static readonly HashSet<string> TypedProperties =
    [
        "power",
        "powered",
        "facing",
        "open",
        "age",
        "waterlogged",
        "enabled",
        "lit",
        "distance",
        "level"
    ];

    /// <summary>
    /// Emits where each typed property sits in the state ids of the blocks that have it. Vanilla numbers a block's
    /// states from its base id over every combination of its property values, with the properties in name order, the
    /// first outermost and the last varying fastest, so a property's value index is
    /// <c>(stateId - baseId) / stride % values</c>.
    /// </summary>
    private static void GenerateTypedPropertyLayouts(Block[] blocks, SourceProductionContext ctx)
    {
        var entries = new CodeBuilder();

        foreach (var block in blocks)
        {
            // Vanilla orders a block's properties by name, as its StateDefinition sorts them.
            var properties = block.Properties
                .OrderBy(property => property.JsonName, StringComparer.Ordinal)
                .ToArray();

            var stride = 1;
            for (var i = properties.Length - 1; i >= 0; i--)
            {
                var property = properties[i];
                if (TypedProperties.Contains(property.JsonName))
                {
                    var values = string.Join(", ", property.JsonValues.Select(value => $"\"{value}\""));
                    entries.Line($"    (\"{property.JsonName}\", {block.RegistryId}, {stride}, [{values}]),");
                }

                stride *= property.JsonValues.Length;
            }
        }

        var builder = new CodeBuilder()
            .Line("#nullable enable")
            .Namespace("Obsidian.Registries")
            .Type("internal static partial class BlocksRegistry")
            .Line("/// <summary>")
            .Line("/// Where each property with a typed handle sits in the state ids of each block that has it: the")
            .Line("/// distance between states that differ only in it (the stride), and its values in vanilla's order.")
            .Line("/// </summary>")
            .Line("internal static readonly (string Property, int RegistryId, int Stride, string[] Values)[] TypedPropertyLayouts =")
            .Line("[")
            .Append(entries.ToString())
            .Line("];")
            .EndScope();

        ctx.AddSource("BlocksRegistry.TypedProperties.g.cs", builder.ToString());
    }
}
