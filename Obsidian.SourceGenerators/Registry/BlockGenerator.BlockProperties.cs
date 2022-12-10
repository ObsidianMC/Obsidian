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

            var builder = new CodeBuilder()
                .Namespace("Obsidian.API")
                .Line()
                .Type($"public enum {name}");

            if (name == "BlockFace")
            {
                builder.Indent().Line($"Down,");
                builder.Indent().Line($"Up,");
                builder.Indent().Line($"North,");
                builder.Indent().Line($"South,");
                builder.Indent().Line($"West,");
                builder.Indent().Line($"East,");
            }
            else
            {
                foreach (var value in values)
                    builder.Indent().Line($"{value},");
            }

            builder.EndScope();

            ctx.AddSource($"{name}.g.cs", builder.ToString());
        }
    }
}
