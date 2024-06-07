using Obsidian.SourceGenerators.Registry.Models;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry;
public partial class RegistryAssetsGenerator
{
    private static void GenerateDimensions(Codec[] dimensions, CodeBuilder builder, SourceProductionContext ctx)
    {
        builder.Type($"public static class Dimensions");

        builder.Indent().Append("public const string CodecKey = \"minecraft:dimension_type\";").Line().Line();

        foreach (var dimension in dimensions)
        {
            var propertyName = dimension.Name.RemoveNamespace().ToPascalCase();
            builder.Indent().Append($"public static DimensionCodec {propertyName} {{ get; }} = new() {{ Id = {dimension.RegistryId}, Name = \"{dimension.Name}\", Element = new() {{ ");

            foreach (var property in dimension.Properties)
            {
                var name = property.Key;
                var value = property.Value;

                builder.Append($"{name} = ");

                if (name == "MonsterSpawnLightLevel")// monster_spawn_light_level is an object and not int
                {
                    ParseMonsterLightValue(builder, value, ctx);
                    continue;
                }

                AppendValueType(builder, value, ctx);
            }

            builder.Append("} };").Line();
        }

        builder.Line().Statement("public static IReadOnlyDictionary<string, DimensionCodec> All { get; } = new Dictionary<string, DimensionCodec>");

        foreach (var name in dimensions.Select(x => x.Name))
        {
            var propertyName = name.RemoveNamespace().ToPascalCase();
            builder.Line($"{{ \"{name}\", {propertyName} }},");
        }

        builder.EndScope(".AsReadOnly()", true).Line();

        builder.EndScope();
    }

    private static void ParseMonsterLightValue(CodeBuilder builder, JsonElement element, SourceProductionContext ctx)
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

                builder.Append($"{name} = ");
                AppendValueType(builder, property.Value, ctx);
            }
            builder.Append("} ");
        }

        builder.Append("}, ");
    }
}
