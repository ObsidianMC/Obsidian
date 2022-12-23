using Obsidian.SourceGenerators.Registry.Models;

namespace Obsidian.SourceGenerators.Registry;

public partial class BlockGenerator
{
    private static void GenerateBlocks(Block[] blocks, GeneratorExecutionContext ctx)
    {
        var blocksBuilder = new CodeBuilder()
            .Using("Obsidian.Blocks")
            .Line()
            .Namespace("Obsidian.Utilities.Registry")
            .Type("internal partial class BlocksRegistry");

        foreach (var block in blocks)
        {
            var blockName = block.Name;

            if (blockName == "Obsidian")
                blockName = "ObsidianBlock";

            var builder = new CodeBuilder()
                .Using("Obsidian.API")
                .Using("Obsidian.API.BlockStates")
                .Using("Obsidian.API.BlockStates.Builders")
                .Line()
                .Namespace("Obsidian.Blocks")
                .Line()
                .Type($"public readonly struct {blockName} : IBlock");

            builder.Line($"public string UnlocalizedName => \"{block.Tag}\";");
            builder.Line($"public int BaseId => {block.BaseId};");

            builder.Line($"public Material Material => Material.{block.Name};");

            builder.Line("public IBlockState State { get; init; }");

            builder.Line().Method($"public {blockName}()").EndScope();

            if (block.Properties.Length > 0)
            {
                builder.Line().Method($"public {blockName}(int stateId)");
                builder.Line($"this.State = new {blockName}StateBuilder(stateId).Build();");
                builder.EndScope();

                builder.Line().Method($"public {blockName}(IBlockState state)");
                builder.Line("ArgumentNullException.ThrowIfNull(state, \"state\");");
                builder.Line("this.State = state;").EndScope();
            }



            builder.Line().Method("public override int GetHashCode()").Line($"return this.State != null ? this.State.Id : this.BaseId;").EndScope();

            builder.EndScope();
            ctx.AddSource($"{blockName}.g.cs", builder.ToString());

            blocksBuilder.Indent().Append($"public static readonly IBlock {blockName} = new {blockName}();").Line();
        }

        blocksBuilder.EndScope();

        ctx.AddSource("BlocksRegistry.Blocks.g.cs", blocksBuilder.ToString());
    }
}
