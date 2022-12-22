using Obsidian.SourceGenerators.Registry.Models;

namespace Obsidian.SourceGenerators.Registry;
public partial class BlockGenerator
{
    private static void CreateBlockStates(Block[] blocks, GeneratorExecutionContext ctx)
    {
        foreach (var block in blocks)
        {
            if (block.Properties.Length == 0)
                continue;

            var blockName = block.Name;

            var builder = new CodeBuilder()
                .Namespace("Obsidian.API.BlockStates")
                .Line()
                .Type($"public readonly struct {blockName}State : IBlockState");

            builder.Indent().Append("public int Id { get; internal init; } = ").Append($"{block.DefaultId};");

            foreach (var property in block.Properties)
            {
                var name = property.Name;

                builder.Line().Indent().Append($"public required {property.Type} {name} ").Append("{ get; init; }");
            }

            builder.Line().Line().Method($"public {blockName}State()").EndScope();

            //00010
            builder.EndScope();

            ctx.AddSource($"{blockName}State.g.cs", builder.ToString());
        }
    }
}
