namespace Obsidian.SourceGenerators.Registry;

[Generator]
public sealed class RegistryAssetsGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
    }

    public void Execute(GeneratorExecutionContext context)
    {
        var source = new CodeBuilder();

        source.Using("System");
        source.Line();
        source.Namespace("Obsidian.Util.Registry");
        source.Type("public static partial class Registry");

        ParseBiomeDimensionCodecs(GetJson("biome_dimension_codec.json"), source);
        ParseBlocks(GetJson("blocks.json"), source);
        ParseDimensions(GetJson("default_dimensions.json"), source);
        ParseEntityTypes(GetJson("entity_types.json"), source);
        ParseFluids(GetJson("fluids.json"), source);
        ParseItems(GetJson("items.json"), source);
        ParseRecipes(GetJson("recipes.json"), source);
        ParseTags(GetJson("tags.json"), source);
        ParsePacketDebug(GetJson("packet_debug.json"), source);

        source.EndScope(); // EOF type
        source.EndScope(); // EOF namespace

        context.AddSource("Registry.Assets.cs", source.ToString());
    }

    private static void ParseBiomeDimensionCodecs(string json, CodeBuilder source)
    {

    }

    private static void ParseBlocks(string json, CodeBuilder source)
    {

    }

    private static void ParseDimensions(string json, CodeBuilder source)
    {

    }

    private static void ParseEntityTypes(string json, CodeBuilder source)
    {

    }

    private static void ParseFluids(string json, CodeBuilder source)
    {

    }

    private static void ParseItems(string json, CodeBuilder source)
    {

    }

    private static void ParseRecipes(string json, CodeBuilder source)
    {

    }

    private static void ParseTags(string json, CodeBuilder source)
    {

    }
    
    private static void ParsePacketDebug(string json, CodeBuilder source)
    {

    }

    private static string GetJson(string file)
    {
        throw new NotImplementedException();
    }
}
