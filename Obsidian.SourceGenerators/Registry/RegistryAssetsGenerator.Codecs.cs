using Obsidian.SourceGenerators.Registry.Models;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry;
public partial class RegistryAssetsGenerator
{
    //This will have to saved as a seperate PR as it would require me to re-work this entire gen to make it work
    //TODO COME BACK TO THIS
    private static string[] BlacklistedBiomeProperties = ["Features", "Carvers", "Spawners", "SpawnCosts"];

    private static void GenerateCodecs(Assets assets, SourceProductionContext ctx)
    {
        var builder = new CodeBuilder()
            .Using("Obsidian.API.Registry.Codecs")
            .Using("Obsidian.API.Registry.Codecs.Biomes")
            .Using("Obsidian.API.Registry.Codecs.Chat")
            .Using("Obsidian.API.Registry.Codecs.Dimensions")
            .Using("Obsidian.API.Registry.Codecs.DamageTypes")
            .Using("Obsidian.API.Registry.Codecs.ArmorTrims")
            .Using("Obsidian.API.Registry.Codecs.ArmorTrims.TrimPattern")
            .Using("Obsidian.API.Registry.Codecs.ArmorTrims.TrimMaterial")
            .Using("Obsidian.API.Registry.Codecs.WolfVariant")
            .Using("Obsidian.API.Registry.Codecs.PaintingVariant")
            .Using("System.Collections.Frozen")
            .Line()
            .Namespace("Obsidian.API.Registries")
            .Line()
            .Type("public static partial class CodecRegistry");

        var codecs = assets.Codecs;

        builder.GenerateSimpleCodec(codecs["dimensions"].ToArray(), "Dimensions", "minecraft:dimension_type", "DimensionCodec",
           (name, value) =>
           {
               builder.Append($"{name} = ");

               if (name == "MonsterSpawnLightLevel")// monster_spawn_light_level is an object and not int
               {
                   builder.ParseMonsterLightValue(value, ctx);
                   return;
               }

               builder.AppendValueType(value, ctx);
           }, ctx);

        builder.GenerateSimpleCodec(codecs["biomes"].ToArray(), "Biomes", "minecraft:worldgen/biome", "BiomeCodec",
            (name, value) =>
            {
                if (BlacklistedBiomeProperties.Contains(name))
                {
                    builder.Append($"{name} = [],");
                    return;
                }

                builder.Append($"{name} = ");

                if (value.ValueKind == JsonValueKind.Object)
                {
                    builder.ParseProperty(value, ctx);
                    return;
                }
                else if (value.ValueKind == JsonValueKind.Array)
                {
                    builder.ParseArray(value, ctx);
                    return;
                }

                builder.AppendValueType(value, ctx);
            }, ctx);

        builder.GenerateSimpleCodec(codecs["chat_type"].ToArray(), "ChatType", "minecraft:chat_type", "ChatTypeCodec", ctx);

        builder.GenerateSimpleCodec(codecs["damage_type"].ToArray(), "DamageType", "minecraft:damage_type", "DamageTypeCodec",
            (name, value) =>
            {
                builder.Append($"{name} = ");

                if (value.ValueKind == JsonValueKind.String && name != "MessageId")
                {
                    name = name switch
                    {
                        "Scaling" => "DamageScaling",
                        "Effects" => "DamageEffects",
                        _ => name
                    };

                    builder.Append($"{name}.{value.GetString()!.ToPascalCase()}, ");

                    return;
                }

                builder.AppendValueType(value, ctx);
            }, ctx);

        builder.GenerateSimpleCodec(codecs["trim_material"].ToArray(), "TrimMaterial", "minecraft:trim_material", "TrimMaterialCodec",
            (name, value) =>
            {
                builder.Append($"{name} = ");

                if (value.ValueKind == JsonValueKind.Object)
                {
                    builder.ParseProperty(value, ctx, name == "OverrideArmorAssets");
                    return;
                }

                builder.AppendValueType(value, ctx, name == "OverrideArmorAssets");
            }, ctx);

        builder.GenerateSimpleCodec(codecs["trim_pattern"].ToArray(), "TrimPattern", "minecraft:trim_pattern", "TrimPatternCodec", ctx);
        builder.GenerateSimpleCodec(codecs["wolf_variant"].ToArray(), "WolfVariant", "minecraft:wolf_variant", "WolfVariantCodec", ctx);
        builder.GenerateSimpleCodec(codecs["painting_variant"].ToArray(), "PaintingVariant", "minecraft:painting_variant", "PaintingVariantCodec", ctx);

        builder.EndScope();

        ctx.AddSource("CodecRegistry.g.cs", builder.ToString());
    }
}
