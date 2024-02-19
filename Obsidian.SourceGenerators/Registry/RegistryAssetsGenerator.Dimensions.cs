using Obsidian.SourceGenerators.Registry.Models;

namespace Obsidian.SourceGenerators.Registry;
public partial class RegistryAssetsGenerator
{
    private static void GenerateDimensions(Codec[] dimensions, CodeBuilder builder)
    {
        builder.Type($"public static partial class Dimensions");

        builder.Indent()
            .Append("public const string CodecKey = \"minecraft:dimension_type\";").Line().Line();

        builder.Indent()
            .Append($"public const int GlobalBitsPerEntry = {(int)Math.Ceiling(Math.Log(dimensions.Length, 2))};").Line().Line();

        foreach (var codec in dimensions)
        {
            var propertyName = codec.Name.RemoveNamespace().ToPascalCase();

            builder.Indent()
                .Append($"public static DimensionCodec {propertyName} => All[\"{codec.Name}\"];")
                .Line();
        }

        builder.Line()
            .Indent()
            .Append("internal static ConcurrentDictionary<string, DimensionCodec> All { get; } = new();")
            .Line();

        builder.Method("internal static async Task InitalizeAsync()");

        builder.Line("var asm = Assembly.GetExecutingAssembly();");
        builder.Line("await using var stream = asm.GetManifestResourceStream($\"{CodecRegistry.AssetsNamespace}.dimensions.json\");");
        builder.Line("var element = await JsonSerializer.DeserializeAsync<JsonElement>(stream, Globals.RegistryJsonOptions);");

        builder.Line("var values = element.GetProperty(\"value\").EnumerateArray().Select(x => x.Deserialize<DimensionCodec>(Globals.RegistryJsonOptions));");

        builder.Statement("foreach(var value in values)");

        builder.Line("All.TryAdd(value.Name, value);");

        builder.EndScope();

        builder.EndScope()
            .Line();

        builder.EndScope();
    }
}
