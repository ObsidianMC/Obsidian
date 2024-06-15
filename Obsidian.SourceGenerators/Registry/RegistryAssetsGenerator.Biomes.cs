using Obsidian.SourceGenerators.Registry.Models;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry;
public partial class RegistryAssetsGenerator
{
    //This will have to saved as a seperate PR as it would require me to re-work this entire gen to make it work
    //TODO COME BACK TO THIS
    private static string[] BlacklistedProperties = ["Features", "Carvers", "Spawners", "SpawnCosts"];

    private static void GenerateBiomes(Codec[] biomes, CodeBuilder builder, SourceProductionContext ctx)
    {
        builder.Type($"public static class Biomes");

        builder.Indent().Append("public const string CodecKey = \"minecraft:worldgen/biome\";").Line().Line();
        builder.Indent().Append($"public const int GlobalBitsPerEntry = {(int)Math.Ceiling(Math.Log(biomes.Length, 2))};").Line().Line();

        foreach (var biome in biomes)
        {
            var propertyName = biome.Name.RemoveNamespace().ToPascalCase();

            builder.Indent().Append($"public static BiomeCodec {propertyName} {{ get; }} = new() {{ Id = {biome.RegistryId}, Name = \"{biome.Name}\", Element = new() {{ ");

            foreach (var property in biome.Properties)
            {
                var name = property.Key;
                var value = property.Value;

                if (BlacklistedProperties.Contains(name))
                {
                    builder.Append($"{name} = [],");
                    continue;
                }

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

        builder.Line().Statement("public static IReadOnlyDictionary<string, BiomeCodec> All { get; } = new Dictionary<string, BiomeCodec>");

        foreach (var name in biomes.Select(x => x.Name))
        {
            var propertyName = name.RemoveNamespace().ToPascalCase();
            builder.Line($"{{ \"{name}\", {propertyName} }},");
        }

        builder.EndScope(".AsReadOnly()", true).Line();

        builder.EndScope();
    }
}
