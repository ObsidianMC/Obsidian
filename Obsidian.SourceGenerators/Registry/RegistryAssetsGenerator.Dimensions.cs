using Obsidian.SourceGenerators.Registry.Models;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry;
public partial class RegistryAssetsGenerator
{
    private static void GenerateDimensions(Dimension[] dimensions, CodeBuilder builder, SourceProductionContext ctx)
    {
        builder.Statement($"internal static CodecCollection<int, DimensionCodec> Dimensions = new(\"minecraft:dimension_type\")");
        foreach (var dimension in dimensions)
        {
            builder.Indent().Append($"{{ {dimension.RegistryId}, new() {{ Id = {dimension.RegistryId}, Name = \"{dimension.Name}\", Element = new() {{ ");

            foreach (var property in dimension.Properties)
            {
                var name = property.Key;
                var value = property.Value;

                builder.Append($"{name} = ");

                if (name == "MonsterSpawnLightLevel")// monster_spawn_light_level is an object and not int
                {
                    ParseMonsterLightValue(builder, value);
                    continue;
                }

                switch (value.ValueKind)
                {
                    case JsonValueKind.String:
                        builder.Append($"\"{value.GetString()}\", ");
                        break;
                    case JsonValueKind.Number:
                    {
                        if (value.TryGetInt32(out var intValue))
                            builder.Append($"{intValue}, ");
                        else if (value.TryGetInt64(out var longValue))
                            builder.Append($"{longValue}, ");
                        else if (value.TryGetSingle(out var floatValue))
                            builder.Append($"{floatValue}f, ");
                        else if (value.TryGetDouble(out var doubleValue))
                            builder.Append($"{doubleValue}d, ");
                        break;
                    }
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        builder.Append($"{value.GetBoolean().ToString().ToLower()}, ");
                        break;
                    case JsonValueKind.Null:
                        break;
                    default:
                        ctx.ReportDiagnostic(DiagnosticSeverity.Error, "Found an invalid property type in json.");
                        break;
                }
            }

            builder.Append("} } },").Line();
        }
        builder.EndScope(true);
    }

    private static void ParseMonsterLightValue(CodeBuilder builder, JsonElement element)
    {
        builder.Append("new() { ");

        if (element.ValueKind == JsonValueKind.Number)
            builder.Append($"IntValue = {element.GetInt32()} ");
        else
        {
            builder.Append("Value = new() { ");
            foreach (var property in element.EnumerateObject())
            {
                var name = property.Name.ToPascalCase();

                if (name == "Value")
                {
                    foreach (var valueProperty in property.Value.EnumerateObject())
                        builder.Append($"{valueProperty.Name.ToPascalCase()} = {valueProperty.Value.GetInt32()}, ");

                    continue;
                }

                builder.Append($"{name} = \"{property.Value.GetString()}\",");
            }
            builder.Append("} ");
        }

        builder.Append("}, ");
    }
}
