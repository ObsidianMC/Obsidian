using Obsidian.SourceGenerators.Registry.Models;

namespace Obsidian.SourceGenerators.Registry;
public partial class RegistryAssetsGenerator
{
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
            .Using("System.Reflection")
            .Using("System.Text.Json")
            .Line()
            .Namespace("Obsidian.Registries")
            .Line()
            .Type("public static partial class CodecRegistry");

        var codecs = assets.Codecs;

        GenerateDimensions(codecs["dimensions"].ToArray(), builder);
        GenerateBiomes(codecs["biomes"].ToArray(), builder);
        GenerateChatType(codecs["chat_type"].ToArray(), builder);
        GenerateDamageTypes(codecs["damage_type"].ToArray(), builder);
        GenerateTrimMaterial(codecs["trim_material"].ToArray(), builder);
        GenerateTrimPattern(codecs["trim_pattern"].ToArray(), builder);

        builder.EndScope();

        ctx.AddSource("CodecRegistry.g.cs", builder.ToString());
    }

    
}
