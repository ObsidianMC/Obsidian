using Obsidian.SourceGenerators.Registry.Models;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry;
public partial class RegistryAssetsGenerator
{
    private static void GenerateBiomes(Biome[] biomes, CodeBuilder builder, SourceProductionContext ctx)
    {
        builder.Statement($"internal static CodecCollection<int, BiomeCodec> Dimensions = new(\"minecraft:worldgen/biome\")");
        foreach (var biome in biomes)
        {
            builder.Indent().Append($"{{ {biome.RegistryId}, new() {{ Id = {biome.RegistryId}, Name = \"{biome.Name}\", Element = new() {{ ");

            foreach (var property in biome.Properties)
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
}
