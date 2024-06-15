using Obsidian.SourceGenerators.Registry.Models;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry;
public partial class RegistryAssetsGenerator
{
    private static void GenerateWolfVariants(Codec[] wolfVariants, CodeBuilder builder, SourceProductionContext ctx)
    {
        builder.Type($"public static class WolfVariant");

        builder.Indent().Append("public const string CodecKey = \"minecraft:wolf_variant\";").Line().Line();
        builder.Indent().Append($"public const int GlobalBitsPerEntry = {(int)Math.Ceiling(Math.Log(wolfVariants.Length, 2))};").Line().Line();

        foreach (var wolfVariant in wolfVariants)
        {
            var propertyName = wolfVariant.Name.RemoveNamespace().ToPascalCase();

            builder.Indent().Append($"public static WolfVariantCodec {propertyName} {{ get; }} = new() {{ Id = {wolfVariant.RegistryId}, Name = \"{wolfVariant.Name}\", Element = new() {{ ");

            foreach (var property in wolfVariant.Properties)
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

        builder.Line().Statement("public static FrozenDictionary<string, WolfVariantCodec> All { get; } = new Dictionary<string, WolfVariantCodec>");

        foreach (var name in wolfVariants.Select(x => x.Name))
        {
            var propertyName = name.RemoveNamespace().ToPascalCase();
            builder.Line($"{{ \"{name}\", {propertyName} }},");
        }

        builder.EndScope(".ToFrozenDictionary()", true).Line();

        builder.EndScope();
    }
}
