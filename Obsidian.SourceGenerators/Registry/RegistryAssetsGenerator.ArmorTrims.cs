using Obsidian.SourceGenerators.Registry.Models;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry;
public partial class RegistryAssetsGenerator
{
    private static void GenerateTrimMaterial(Codec[] trimMaterials, CodeBuilder builder, SourceProductionContext ctx)
    {
        builder.Type($"public static class TrimMaterials");

        builder.Indent().Append("public const string CodecKey = \"minecraft:trim_material\";").Line().Line();
        builder.Indent().Append($"public const int GlobalBitsPerEntry = {(int)Math.Ceiling(Math.Log(trimMaterials.Length, 2))};").Line().Line();

        foreach (var trimMaterial in trimMaterials)
        {
            var propertyName = trimMaterial.Name.RemoveNamespace().ToPascalCase();

            builder.Indent().Append($"public static TrimMaterialCodec {propertyName} {{ get; }} = new() {{ Id = {trimMaterial.RegistryId}, Name = \"{trimMaterial.Name}\", Element = new() {{ ");

            foreach (var property in trimMaterial.Properties)
            {
                var name = property.Key;
                var value = property.Value;

                builder.Append($"{name} = ");

                if (value.ValueKind == JsonValueKind.Object)
                {
                    ParseProperty(builder, value, ctx, name == "OverrideArmorMaterials");
                    continue;
                }

                AppendValueType(builder, value, ctx, name == "OverrideArmorMaterials");
            }

            builder.Append("} };").Line();
        }

        builder.Line().Statement("public static IReadOnlyDictionary<string, TrimMaterialCodec> All { get; } = new Dictionary<string, TrimMaterialCodec>");

        foreach (var name in trimMaterials.Select(x => x.Name))
        {
            var propertyName = name.RemoveNamespace().ToPascalCase();
            builder.Line($"{{ \"{name}\", {propertyName} }},");
        }

        builder.EndScope(".AsReadOnly()", true).Line();

        builder.EndScope();
    }

    private static void GenerateTrimPattern(Codec[] trimPatterns, CodeBuilder builder, SourceProductionContext ctx)
    {
        builder.Type($"public static class TrimPatterns");

        builder.Indent().Append("public const string CodecKey = \"minecraft:trim_pattern\";").Line().Line();
        builder.Indent().Append($"public const int GlobalBitsPerEntry = {(int)Math.Ceiling(Math.Log(trimPatterns.Length, 2))};").Line().Line();

        foreach (var trimPattern in trimPatterns)
        {
            var propertyName = trimPattern.Name.RemoveNamespace().ToPascalCase();

            builder.Indent().Append($"public static TrimPatternCodec {propertyName} {{ get; }} = new() {{ Id = {trimPattern.RegistryId}, Name = \"{trimPattern.Name}\", Element = new() {{ ");

            foreach (var property in trimPattern.Properties)
            {
                var name = property.Key;
                var value = property.Value;

                builder.Append($"{name} = ");

                if (value.ValueKind == JsonValueKind.Object)
                {
                    ParseProperty(builder, value, ctx);
                    continue;
                }

                AppendValueType(builder, value, ctx);
            }

            builder.Append("} };").Line();
        }

        builder.Line().Statement("public static IReadOnlyDictionary<string, TrimPatternCodec> All { get; } = new Dictionary<string, TrimPatternCodec>");

        foreach (var name in trimPatterns.Select(x => x.Name))
        {
            var propertyName = name.RemoveNamespace().ToPascalCase();
            builder.Line($"{{ \"{name}\", {propertyName} }},");
        }

        builder.EndScope(".AsReadOnly()", true).Line();

        builder.EndScope();
    }
}
