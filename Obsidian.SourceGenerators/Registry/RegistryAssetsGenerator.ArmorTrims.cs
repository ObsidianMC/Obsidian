using Obsidian.SourceGenerators.Registry.Models;

namespace Obsidian.SourceGenerators.Registry;
public partial class RegistryAssetsGenerator
{
    private static void GenerateTrimMaterial(Codec[] trimMaterials, CodeBuilder builder)
    {
        builder.Type($"public static partial class TrimMaterials");

        builder.Indent().
            Append("public const string CodecKey = \"minecraft:trim_material\";").Line().Line();
        builder.Indent()
            .Append($"public const int GlobalBitsPerEntry = {(int)Math.Ceiling(Math.Log(trimMaterials.Length, 2))};").Line().Line();

        foreach (var codec in trimMaterials)
        {
            var propertyName = codec.Name.RemoveNamespace().ToPascalCase();

            builder.Indent()
                .Append($"public static TrimMaterialCodec {propertyName} => All[\"{codec.Name}\"];")
                .Line();
        }

        builder.Line()
            .Indent()
            .Append("internal static ConcurrentDictionary<string, TrimMaterialCodec> All { get; } = new();")
            .Line();

        builder.EndScope();
    }

    private static void GenerateTrimPattern(Codec[] trimPatterns, CodeBuilder builder)
    {
        builder.Type($"public static partial class TrimPatterns");

        builder.Indent().Append("public const string CodecKey = \"minecraft:trim_pattern\";").Line().Line();
        builder.Indent().Append($"public const int GlobalBitsPerEntry = {(int)Math.Ceiling(Math.Log(trimPatterns.Length, 2))};").Line().Line();

        foreach (var codec in trimPatterns)
        {
            var propertyName = codec.Name.RemoveNamespace().ToPascalCase();

            builder.Indent().Append($"public static TrimPatternCodec {propertyName} => All[\"{codec.Name}\"];").Line();
        }

        builder.Line().Indent().Append("internal static ConcurrentDictionary<string, TrimPatternCodec> All { get; } = new();");

        builder.EndScope();
    }
}
