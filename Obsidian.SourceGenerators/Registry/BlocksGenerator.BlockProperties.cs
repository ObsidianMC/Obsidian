using Obsidian.SourceGenerators.Registry.Models;

namespace Obsidian.SourceGenerators.Registry;
public partial class BlocksGenerator
{
    private static void GenerateBlocksProperties(SourceProductionContext ctx)
    {
        foreach (var cache in BlockProperty.enumValuesCache)
        {
            var name = cache.Key;
            var values = cache.Value;

            if (name == "BlockFace")
                continue;

            var builder = new CodeBuilder()
                .Line(ApiDocs.MissingDocsPragma)
                .Namespace("Obsidian.API")
                .Line()
                .XmlSummary(ApiDocs.BlockProperty(name))
                .Type($"public enum {name}");

            foreach (var value in values)
                builder.Line($"{value},");

            builder.EndScope();

            ctx.AddSource($"{name}.g.cs", builder.ToString());
        }
    }
}
