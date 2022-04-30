using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

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

        var gen = (Action<string, GeneratorExecutionContext> generator, string file) =>
        {
            string? json = GetJson(file, context);
            if (json is not null)
                generator(json, context);
        };

        gen(ParseBiomeDimensionCodecs, "biome_dimension_codec.json");
        gen(ParseBlocks, "blocks.json");
        gen(ParseDimensions, "default_dimensions.json");
        gen(ParseEntityTypes, "entity_types.json");
        gen(ParseFluids, "fluids.json");
        gen(ParseItems, "items.json");
        gen(ParseRecipes, "recipes.json");
        gen(ParseTags, "tags.json");

        source.EndScope(); // end of type

        context.AddSource("Registry.Assets.cs", source.ToString());
    }

    private static void ParseBiomeDimensionCodecs(string json, GeneratorExecutionContext context)
    {

    }

    private static void ParseBlocks(string json, GeneratorExecutionContext context)
    {

    }

    private static void ParseDimensions(string json, GeneratorExecutionContext context)
    {

    }

    private static void ParseEntityTypes(string json, GeneratorExecutionContext context)
    {

    }

    private static void ParseFluids(string json, GeneratorExecutionContext context)
    {

    }

    private static void ParseItems(string json, GeneratorExecutionContext context)
    {

    }

    private static void ParseRecipes(string json, GeneratorExecutionContext context)
    {

    }

    private static void ParseTags(string json, GeneratorExecutionContext context)
    {
        var tags = JsonSerializer.Deserialize<Dictionary<string, Tag>>(json) ?? new();

        var builder = new CodeBuilder();
        builder.Namespace("Obsidian.Util.Registry");
        builder.Line();
        builder.Type("public static class TagsRegistry");

        builder.Line($"// Tag Count = {tags.Count}");
        foreach (var tag in tags)
        {
            builder.Line($"// {tag.Key}: {tag.Value}");
            builder.Line($"// {string.Join(", ", tag.Value.Values)}");
        }

        builder.EndScope();
        context.AddSource("TagsRegistry.g.cs", builder.ToString());
    }

    private static string? GetJson(string file, GeneratorExecutionContext context)
    {
        return context.AdditionalFiles.FirstOrDefault(additionalText => Path.GetFileName(additionalText.Path).Equals(file))?.GetText()?.ToString();
    }

    private sealed class Tag
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("type")]
        public string Type { get; set; }
        [JsonPropertyName("replace")]
        public bool Replace { get; set; }
        [JsonPropertyName("values")]
        public List<string> Values { get; set; }

        public override string ToString()
        {
            return $"Tag {{ Name = \"{Name}\", Type = \"{Type}\", Replace = {Replace}, Values Count = {Values?.Count ?? 0} }}";
        }
    }
}
