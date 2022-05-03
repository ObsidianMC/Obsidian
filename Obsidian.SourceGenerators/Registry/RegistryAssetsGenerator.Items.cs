using Obsidian.SourceGenerators.Registry.Models;

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
        foreach (Item item in assets.Items)
        {
            builder.Line($"{{ Material.{item.Name}, new Item({item.Id}, \"{item.Tag}\", Material.{item.Name}) }},");
        }
        builder.EndScope(semicolon: true);

        builder.EndScope();

        context.AddSource("ItemsRegistry.g.cs", builder.ToString());
    }
}
