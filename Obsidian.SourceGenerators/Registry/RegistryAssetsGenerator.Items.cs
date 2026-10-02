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
            builder.Line($"public static Item {name} {{ get; }} = new Item({item.Id}, \"{item.Tag}\", Material.{name}){initializer};");
        }

        builder.Statement("internal static FrozenDictionary<Material, Item> Items = new Dictionary<Material, Item>()");

        foreach (Item item in assets.Items)
        {
            var name = item.Name;

            builder.Line($"{{ Material.{name}, {name} }},");
        }

        builder.EndScope(".ToFrozenDictionary()", semicolon: true);

        builder.EndScope();

        context.AddSource("ItemsRegistry.g.cs", builder.ToString());
    }
}
