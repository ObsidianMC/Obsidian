using Obsidian.SourceGenerators.Registry.Models;

namespace Obsidian.SourceGenerators.Registry;
public partial class RegistryAssetsGenerator
{
    private static void GenerateBiomes(Codec[] biomes, CodeBuilder builder)
    {
        builder.Type($"public static partial class Biomes");

        builder.Indent()
            .Append("public const string CodecKey = \"minecraft:worldgen/biome\";").Line().Line();
        builder.Indent()
            .Append($"public const int GlobalBitsPerEntry = {(int)Math.Ceiling(Math.Log(biomes.Length, 2))};").Line().Line();

        foreach (var biome in biomes)
        {
            var propertyName = biome.Name.RemoveNamespace().ToPascalCase();

            builder.Indent()
                .Append($"public static BiomeCodec {propertyName} => All[\"{biome.Name}\"];")
                .Line();
        }

        builder.Line()
            .Indent()
            .Append("internal static ConcurrentDictionary<string, BiomeCodec> All { get; } = new();")
            .Line();

        builder.Method("internal static async Task InitalizeAsync()");

        builder.Line("var asm = Assembly.GetExecutingAssembly();");
        builder.Line("await using var stream = asm.GetManifestResourceStream($\"{CodecRegistry.AssetsNamespace}.biomes.json\");");
        builder.Line("var element = await JsonSerializer.DeserializeAsync<JsonElement>(stream, Globals.RegistryJsonOptions);");

        builder.Line("var values = element.GetProperty(\"value\").EnumerateArray().Select(x => x.Deserialize<BiomeCodec>(Globals.RegistryJsonOptions));");

        builder.Statement("foreach(var value in values)");

        builder.Line("All.TryAdd(value.Name, value);");

        builder.EndScope();

        builder.EndScope()
            .Line();

        builder.EndScope();
    }

}
