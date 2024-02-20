using Obsidian.SourceGenerators.Registry.Models;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry;
public partial class WorldgenRegistryGenerator
{
    private static void BuildTreeType(Dictionary<string, TypeInformation> trunkPlacers, Features features, CodeBuilder builder)
    {
        var treeFeatures = features.TreeFeatures;

        foreach (var treeFeature in treeFeatures)
        {
            var sanitizedName = treeFeature.Name.ToPascalCase();
            builder.Type($"public static readonly TreeFeature {sanitizedName} = new()");

            builder.Line($"Identifier = \"{treeFeature.Name}\",");

            foreach (var property in treeFeature.Properties)
            {
                var name = property.Name;
                var value = property.Value;
                switch (value.ValueKind)
                {
                    case JsonValueKind.String:
                        builder.Line($"{name.ToPascalCase()} = \"{value.GetString()}\",");
                        break;
                    case JsonValueKind.Number:
                        break;
                    case JsonValueKind.Array:
                        builder.Line($"{name.ToPascalCase()} = {{}},");
                        break;
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        builder.Line($"{name.ToPascalCase()} = {value.GetBoolean().ToString().ToLower()},");
                        break;
                    default:
                        {
                            if (value.TryGetProperty("type", out var typeElement) &&
                               trunkPlacers.TryGetValue(typeElement.GetString()!, out var trunkPlacer))
                            {
                                builder.Line($"{name.ToPascalCase()} = new {trunkPlacer.Symbol.Name}() {{ Type = \"{trunkPlacer.ResourceLocation}\" }},");
                                break;
                            }

                            builder.Line($"{name.ToPascalCase()} = new(),");
                            break;
                        }
                }
            }

            builder.EndScope(true);
        }
    }
}
