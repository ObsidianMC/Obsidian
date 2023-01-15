using Obsidian.SourceGenerators.Registry.Models;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry;
public partial class RegistryAssetsGenerator
{
    private static void GenerateBiomes(Codec[] biomes, CodeBuilder builder, SourceProductionContext ctx)
    {
        builder.Type($"public static class Biomes");

        builder.Indent().Append("public const string CodecKey = \"minecraft:worldgen/biome\";").Line().Line();

        builder.Statement("public static IReadOnlyDictionary<string, BiomeCodec> All { get; } = new Dictionary<string, BiomeCodec>");

        foreach (var name in biomes.Select(x => x.Name))
        {
            var propertyName = name.RemoveNamespace().ToPascalCase();
            builder.Line($"{{ \"{name}\", {propertyName} }},");
        }

        builder.EndScope(".AsReadOnly()", true).Line();

        foreach (var biome in biomes)
        {
            var propertyName = biome.Name.RemoveNamespace().ToPascalCase();

            builder.Indent().Append($"public static BiomeCodec {propertyName} {{ get; }} = new() {{ Id = {biome.RegistryId}, Name = \"{biome.Name}\", Element = new() {{ ");

            foreach (var property in biome.Properties)
            {
                var name = property.Key;
                var value = property.Value;

                builder.Append($"{name} = ");

                if (value.ValueKind == JsonValueKind.Object)
                {
                    ParseBiomeProperty(builder, value, ctx);
                    continue;
                }

                AppendValueType(builder, value, ctx);
            }

            builder.Append("} };").Line();
        }
        builder.EndScope();
    }

    private static void ParseBiomeProperty(CodeBuilder builder, JsonElement element, SourceProductionContext ctx)
    {
        builder.Append("new() { ");

        foreach(var property in element.EnumerateObject())
        {
            var name = property.Name.ToPascalCase();
            var value = property.Value;

            builder.Append($"{name} = ");

            if (value.ValueKind == JsonValueKind.Object)
            {
                ParseBiomeProperty(builder, value, ctx);
                continue;
            }

            AppendValueType(builder, value, ctx);
        }

        builder.Append("}, ");
    }

    private static void AppendValueType(CodeBuilder builder, JsonElement element, SourceProductionContext ctx)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                builder.Append($"\"{element.GetString()}\", ");
                break;
            case JsonValueKind.Number:
            {
                if (element.TryGetInt32(out var intValue))
                    builder.Append($"{intValue}, ");
                else if (element.TryGetInt64(out var longValue))
                    builder.Append($"{longValue}, ");
                else if (element.TryGetSingle(out var floatValue))
                    builder.Append($"{floatValue}f, ");
                else if (element.TryGetDouble(out var doubleValue))
                    builder.Append($"{doubleValue}d, ");
                break;
            }
            case JsonValueKind.True:
            case JsonValueKind.False:
                builder.Append($"{element.GetBoolean().ToString().ToLower()}, ");
                break;
            case JsonValueKind.Null:
                break;
            default:
                ctx.ReportDiagnostic(DiagnosticSeverity.Error, "Found an invalid property type in json.");
                break;
        }
    }
}
