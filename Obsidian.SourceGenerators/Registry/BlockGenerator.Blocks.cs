using Obsidian.SourceGenerators.Registry.Models;
using System.Text;

namespace Obsidian.SourceGenerators.Registry;

public partial class BlockGenerator
{

    private static void GenerateButtons(Block[] blocks, GeneratorExecutionContext ctx)
    {
        var buttons = new Dictionary<string, Block>();

        foreach (var block in blocks)
        {
            var blockName = block.Name;

            if (blockName.EndsWith("Button"))
            {
                buttons.Add(blockName.Replace("Button", string.Empty), block);
                continue;
            }
        }

        var buttonTypeBuilder = new CodeBuilder()
              .Namespace("Obsidian.API")
              .Line()
              .Type($"public enum ButtonType");

        foreach (var buttonType in buttons.Keys)
            buttonTypeBuilder.Line($"{buttonType},");

        buttonTypeBuilder.EndScope();

        ctx.AddSource("ButtonType.g.cs", buttonTypeBuilder.ToString());

        var button = buttons.First();

        var buttonBlock = button.Value;

        var buttonBuilder = new CodeBuilder()
               .Using("Obsidian.API")
               .Using("Obsidian.API.BlockStates")
               .Line()
               .Namespace("Obsidian.Blocks")
               .Line()
               .Type($"public struct ButtonBlock : IBlock");

        buttonBuilder.Line("public int BaseId { get; private set; }");
        buttonBuilder.Line("public string UnlocalizedName { get; private set; }");
        buttonBuilder.Line("public IBlockState State { get; private set; }");
        buttonBuilder.Line("public ButtonType Type { get; private set; }");
        buttonBuilder.Line("public Material Material => Material.Button;");

        foreach (var property in buttonBlock.Properties)
            buttonBuilder.Line().Indent().Append($"public {property.Type} {property.Name} ").Append("{ get; private set; }");

        buttonBuilder.Line().Method("public ButtonBlock()").EndScope();

        buttonBuilder.Line().Method("public override int GetHashCode()").Line($"return this.State != null ? this.State.Id : this.BaseId;").EndScope();
        
        buttonBuilder.EndScope();
        ctx.AddSource("ButtonBlock.g.cs", buttonBuilder.ToString());
    }

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

            if (blockName.EndsWith("Button"))
                continue;

            if (blockName == "Obsidian")
                blockName = "ObsidianBlock";

            var builder = new CodeBuilder()
                .Using("Obsidian.API")
                .Using("Obsidian.API.BlockStates")
                .Line()
                .Namespace("Obsidian.Blocks")
                .Line()
                .Type($"public struct {blockName} : IBlock");

            builder.Line($"public string UnlocalizedName => \"{block.Tag}\";");
            builder.Line($"public int BaseId => {block.BaseId};");
            builder.Line("public int StateId { get; private set; }");

            builder.Line($"public Material Material => Material.{block.Name};");

            var defaultStateId = new StringBuilder().Append("{ Id = ").Append(block.DefaultId).Append(" }");

            var state = block.Properties.Length == 0 ? "null;" : $"new {blockName}State(){defaultStateId};";

            builder.Line($"public IBlockState State => {state}");

            builder.Line().Method($"public {blockName}()").EndScope();

            builder.Line().Method("public override int GetHashCode()").Line($"return this.State != null ? this.State.Id : this.BaseId;").EndScope();

            builder.EndScope();
            ctx.AddSource($"{blockName}.g.cs", builder.ToString());

            blocksBuilder.Indent().Append($"public static readonly IBlock {blockName} = new {blockName}();").Line();
        }

        blocksBuilder.EndScope();

        ctx.AddSource("BlocksRegistry.Blocks.g.cs", blocksBuilder.ToString());
    }

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
                .Type($"public struct {blockName}State : IBlockState");

            builder.Line("public int Id { get; internal init; }");

            foreach (var property in block.Properties)
            {
                var name = property.Name;
                if (name == "Note")
                    name = "NoteType";

                builder.Line().Indent().Append($"public {property.Type} {name} ").Append("{ get; private set; }");
            }

            builder.Line().Method($"public {blockName}State()").EndScope();

            builder.EndScope();

            ctx.AddSource($"{blockName}State.g.cs", builder.ToString());
        }
    }
}
