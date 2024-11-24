using Obsidian.SourceGenerators.Registry.Models;

namespace Obsidian.SourceGenerators.Registry;

public partial class RegistryAssetsGenerator
{
    private static void GenerateItems(Assets assets, SourceProductionContext context)
    {
        var builder = new CodeBuilder();
        builder.Using("System.Collections.Generic");
        builder.Using("Obsidian.API");
        builder.Using("Obsidian.API.Inventory");
        builder.Line();
        builder.Namespace("Obsidian.Registries");
        builder.Line();
        builder.Type("public static partial class ItemsRegistry");

        builder.Statement("internal static Dictionary<Material, Item> Items = new()");

        foreach (Item item in assets.Items)
        {
            var name = item.Name;

            builder.Line($"{{ Material.{name}, new Item({item.Id}, \"{item.Tag}\", Material.{name}) }},");
        }

        builder.EndScope(semicolon: true);

        builder.EndScope();

        context.AddSource("ItemsRegistry.g.cs", builder.ToString());
    }
}
