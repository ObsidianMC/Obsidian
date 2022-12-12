using Obsidian.SourceGenerators.Registry.Models;
using System.Drawing;

namespace Obsidian.SourceGenerators.Registry;

public partial class RegistryAssetsGenerator
{
    private static void GenerateItems(Assets assets, GeneratorExecutionContext context)
    {
        var builder = new CodeBuilder();
        builder.Using("System.Collections.Generic");
        builder.Using("Obsidian.API");
        builder.Using("Obsidian.Items");
        builder.Line();
        builder.Namespace("Obsidian.Utilities.Registry");
        builder.Line();
        builder.Type("public static class ItemsRegistry");

        builder.Statement("internal static Dictionary<Material, Item> Items = new()");

        var added = new HashSet<string>();
        var addedButton = false;
        foreach (Item item in assets.Items)
        {
            var name = item.Name;

            var match = BlockGenerator.colorRegex.Match(name);

            if (match.Success && !BlockGenerator.ignored.Contains(name))
            {
                var color = match.Value;
                var newName = name.Replace(color, string.Empty);

                if (BlockGenerator.filters.Contains(newName))
                    newName = $"Colored{newName}";

                if (!added.Add(newName))
                    continue;

                builder.Line($"{{ Material.{newName}, new Item({item.Id}, \"{item.Tag}\", Material.{newName}) }},");

                continue;
            }

            if (name.EndsWith("Button") && !addedButton)
            {
                builder.Line($"{{ Material.Button, new Item({item.Id}, \"{item.Tag}\", Material.Button) }},");
                addedButton = true;
                continue;
            }

            builder.Line($"{{ Material.{name}, new Item({item.Id}, \"{item.Tag}\", Material.{name}) }},");
        }
        builder.EndScope(semicolon: true);

        builder.EndScope();

        context.AddSource("ItemsRegistry.g.cs", builder.ToString());
    }
}
