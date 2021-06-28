using Microsoft.CodeAnalysis;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry
{
    internal sealed class ItemsParser : AssetsParser
    {
        public override string SourceFile => "items.json";
        public override string Name => "Items";

        public override void ParseAsset(GeneratorExecutionContext context, string asset)
        {
            if (context.Compilation.AssemblyName != "Obsidian")
                return;

            var builder = new CodeBuilder();
            builder.Using("System.Collections.Generic");
            builder.Using("Obsidian.API");
            builder.Using("Obsidian.Items");
            builder.Line();
            builder.Namespace("Obsidian.Utilities.Registry");
            builder.Type("public static class ItemsRegistry");
            builder.Statement("internal static Dictionary<Material, Item> Items = new()");

            using var jsonDocument = JsonDocument.Parse(asset);
            foreach (JsonProperty item in jsonDocument.RootElement.EnumerateObject())
            {
                string name = item.Name;
                string material = name.RemoveNamespace().ToPascalCase();
                int protocolId = item.Value.GetProperty("protocol_id").GetInt32();

                builder.Line($"{{ Material.{material}, new Item({protocolId}, \"{name}\", Material.{material}) }},");
            }

            builder.EndScope(semicolon: true);
            builder.EndScope();
            builder.EndScope();

            context.AddSource("ItemsRegistry.cs", builder);
        }
    }
}
