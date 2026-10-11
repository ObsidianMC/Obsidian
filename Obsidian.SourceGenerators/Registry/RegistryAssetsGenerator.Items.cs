using Obsidian.SourceGenerators.Registry.Models;

namespace Obsidian.SourceGenerators.Registry;

public partial class RegistryAssetsGenerator
{
    private static void GenerateItems(Assets assets, SourceProductionContext context)
    {
        var builder = new CodeBuilder();
        builder.Using("System.Collections.Generic");
        builder.Using("System.Collections.Frozen");
        builder.Using("Obsidian.API");
        builder.Using("Obsidian.API.Inventory");
        builder.Line();
        builder.Namespace("Obsidian.API.Registries");
        builder.Line();
        builder.Type("public static partial class ItemsRegistry");

        foreach (Item item in assets.Items)
        {
            var name = item.Name;

            var defaults = new List<string>();
            if (item.MaxStackSize != 64)
                defaults.Add($"MaxStackSize = {item.MaxStackSize}");
            if (item.MaxDamage != 0)
                defaults.Add($"MaxDamage = {item.MaxDamage}");
            if (item.Enchantable != 0)
                defaults.Add($"Enchantable = {item.Enchantable}");

            var initializer = defaults.Count == 0 ? string.Empty : $" {{ {string.Join(", ", defaults)} }}";
            builder.Line($"public static Item {name} {{ get; }} = new Item({item.Id}, \"{item.Tag}\"){initializer};");
        }

        builder.Line();
        builder.Statement("private static readonly Item[] vanillaItems =");

        foreach (Item item in assets.Items)
            builder.Line($"{item.Name},");

        builder.EndScope(semicolon: true);

        builder.EndScope();

        context.AddSource("ItemsRegistry.g.cs", builder.ToString());

        GenerateItemIds(assets, context);
    }

    // Vanilla's item ids as constants, for switches and patterns over items in the server's vanilla code.
    private static void GenerateItemIds(Assets assets, SourceProductionContext context)
    {
        var builder = new CodeBuilder();
        builder.Namespace("Obsidian.API.Registries");
        builder.Line();
        builder.Line("/// <summary>");
        builder.Line("/// Vanilla's item ids (<see cref=\"Obsidian.API.Inventory.Item.Id\"/>), as constants");
        builder.Line("/// to match items by in switches and patterns. Plugins' items have no constant: compare");
        builder.Line("/// them by instance.");
        builder.Line("/// </summary>");
        builder.Type("public static class ItemIds");

        foreach (Item item in assets.Items)
            builder.Line($"public const int {item.Name} = {item.Id};");

        builder.EndScope();

        context.AddSource("ItemIds.g.cs", builder.ToString());
    }
}
