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
            .Line()
            .Namespace("Obsidian.Registries")
            .Line()
            .Type("public static partial class CodecRegistry");

        var codecs = assets.Codecs;

        GenerateDimensions(codecs["dimensions"].ToArray(), builder, ctx);
        GenerateBiomes(codecs["biomes"].ToArray(), builder, ctx);
        GenerateChatType(codecs["chat_type"].ToArray(), builder, ctx);

        builder.EndScope();

        ctx.AddSource("CodecRegistry.g.cs", builder.ToString());
    }

    
}
