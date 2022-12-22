using Obsidian.SourceGenerators.Registry.Models;

namespace Obsidian.SourceGenerators.Registry;
public partial class BlockGenerator
{
    private static void GenerateBlocksProperties(GeneratorExecutionContext ctx)
    {
        foreach (var cache in BlockProperty.enumValuesCache)
        {
            var name = cache.Key;
            var values = cache.Value;

            if (name == "BlockFace")
                continue;

            var builder = new CodeBuilder()
                .Namespace("Obsidian.API")
                .Line()
                .Type($"public enum {name}");

            foreach (var value in values)
                builder.Line($"{value},");

            builder.EndScope();

            ctx.AddSource($"{name}.g.cs", builder.ToString());
        }
    }
}
