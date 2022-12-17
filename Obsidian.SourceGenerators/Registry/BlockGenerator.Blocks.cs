using Obsidian.SourceGenerators.Registry.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace Obsidian.SourceGenerators.Registry;

public partial class BlockGenerator
{
    internal static readonly Regex colorRegex = new("^(White|Orange|Magenta|LightBlue|Yellow|Lime|Pink|Gray|LightGray|Cyan|Purple|Blue|Brown|Green|Red|Black)");

    internal static readonly HashSet<string> ignored = new()
    {
        "BlueIce",
        "RedNetherBricks",
        "RedNetherBrickSlab",
        "RedNetherBrickStairs",
        "RedNetherBrickWall",
        "RedSand",
        "RedSandstone",
        "RedSandstoneSlab",
        "RedSandstoneStairs",
        "RedSandstoneWall",
        "Redstone",
        "RedstoneOre",
        "RedstoneTorch",
        "RedstoneWallTorch",
        "RedstoneBlock",
        "RedstoneLamp",
        "RedstoneWire",
        "RedstoneRepeater",
        "WhiteTulip",
        "RedTulip",
        "OrangeTulip",
        "PinkTulip",
        "RedMushroom",
        "RedMushroomBlock",
        "BrownMushroom",
        "BrownMushroomBlock",
        "Blackstone",
        "BlackstoneSlab",
        "BlackstoneStairs",
        "BlackstoneWall",
        "BlueOrchid"
    };

    internal static readonly HashSet<string> filters = new()
    {
        "Candle",
        "CandleCake",
        "Terracotta",
        "ShulkerBox",
        "WallBanner",
    };

    internal static string[] invalidBlocks = new[]
    {
        "NetherWartBlock",
        "DeadTubeCoralBlock",
        "DeadBrainCoralBlock",
        "DeadBubbleCoralBlock",
        "DeadFireCoralBlock",
        "DeadHornCoralBlock",
        "TubeCoralBlock",
        "BrainCoralBlock",
        "BubbleCoralBlock",
        "FireCoralBlock",
        "HornCoralBlock",
        "Grass",
        "Snow",
        "BrownMushroomBlock",
        "RedMushroomBlock"
    };

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
               .Line()
               .Namespace("Obsidian.Blocks")
               .Line()
               .Type($"public sealed class ButtonBlock : IBlock");

        buttonBuilder.Line("public int BaseId { get; private set; }");
        buttonBuilder.Line("public string UnlocalizedName { get; private set; }");
        buttonBuilder.Line("public IBlockState State { get; private set; }");
        buttonBuilder.Line("public ButtonType Type { get; private set; }");
        buttonBuilder.Line("public Material Material => Material.Button;");

        foreach (var property in buttonBlock.Properties)
            buttonBuilder.Line().Indent().Append($"public {property.Type} {property.Name} ").Append("{ get; private set; }");

        buttonBuilder.Line().Method("internal ButtonBlock()").EndScope();

        buttonBuilder.EndScope();
        ctx.AddSource("ButtonBlock.g.cs", buttonBuilder.ToString());
    }

    private static void GenerateBlocks(Block[] blocks, GeneratorExecutionContext ctx)
    {
        var coloredBlocks = new Dictionary<BlockType, ColoredBlock>();

        foreach (var block in blocks)
        {
            var blockName = block.Name;

            if (blockName.EndsWith("Button"))
                continue;

            var match = colorRegex.Match(blockName);

            if (match.Success && !ignored.Contains(blockName))
            {
                var color = match.Value;

                var na = blockName.Replace(color, string.Empty);

                if (filters.Contains(na))
                    na = $"Colored{na}";

                if (Enum.TryParse<BlockType>(na, true, out var blockType))
                {

                    if (!coloredBlocks.TryGetValue(blockType, out _))
                        coloredBlocks[blockType] = new()
                        {
                            Block = block,
                            Type = blockType,
                            Color = color
                        };

                    continue;
                }
            }
            else if (invalidBlocks.Contains(blockName))
                continue;

            blockName = blockName.Replace("Block", string.Empty);

            var builder = new CodeBuilder()
                .Using("Obsidian.API")
                .Line()
                .Namespace("Obsidian.Blocks")
                .Line()
                .Type($"public sealed class {blockName}Block : IBlock");

            builder.Line($"public string UnlocalizedName => \"{block.Tag}\";");
            builder.Line($"public int BaseId => {block.BaseId};");
            builder.Line("public int StateId { get; private set; }");

            builder.Line($"public Material Material => Material.{blockName};");

            builder.Line($"public IBlockState State => new {blockName}();");

            builder.Line().Method($"internal {blockName}Block()").EndScope();

            builder.EndScope();
            ctx.AddSource($"{blockName}Block.g.cs", builder.ToString());
        }

        CreateColoredBlocks(coloredBlocks, ctx);
    }

    private static void CreateBlockStates(Block[] blocks, GeneratorExecutionContext ctx)
    {
        foreach (var block in blocks)
        {
            if (invalidBlocks.Contains(block.Name))
                continue;

            var blockName = block.Name.Replace("Block", string.Empty);

            var builder = new CodeBuilder()
                .Namespace("Obsidian.API.BlockStates")
                .Line()
                .Type($"public sealed class {blockName} : IBlockState");

            builder.Line("public int Id { get; internal init; }");

            foreach (var property in block.Properties)
            {
                var name = property.Name;
                if (name == "Note")
                    name = "NoteType";

                builder.Line().Indent().Append($"public {property.Type} {name} ").Append("{ get; private set; }");
            }

            builder.Line().Method($"internal {blockName}()").EndScope();

            builder.EndScope();

            ctx.AddSource($"{blockName}.g.cs", builder.ToString());
        }
    }

    private static void CreateColoredBlocks(Dictionary<BlockType, ColoredBlock> blocks, GeneratorExecutionContext ctx)
    {
        foreach (var coloredBlock in blocks.Values)
        {
            var type = coloredBlock.Type;
            var block = coloredBlock.Block;

            var blockBuilder = new CodeBuilder()
               .Using("Obsidian.API")
               .Line()
               .Namespace("Obsidian.Blocks")
               .Line()
               .Type($"public sealed class {type}Block : IBlock");

            blockBuilder.Line($"public int BaseId => {block.BaseId};");
            blockBuilder.Line("public string UnlocalizedName { get; private set; }");
            blockBuilder.Line("public BlockColor Color { get; private set; }");
            blockBuilder.Line($"public Material Material => Material.{type};");

            blockBuilder.Line("public IBlockState State { get; private set; }");

            blockBuilder.Line().Method($"internal {type}Block()").EndScope();

            blockBuilder.EndScope();
            ctx.AddSource($"{type}Block.g.cs", blockBuilder.ToString());
        }
    }

    private struct ColoredBlock
    {
        public string Color { get; set; }

        public BlockType Type { get; set; }

        public Block Block { get; set; }
    }

    private enum BlockType
    {
        None,
        Concrete,
        ConcretePowder,
        ColoredTerracotta,
        GlazedTerracotta,
        ColoredCandle,
        ColoredCandleCake,
        Carpet,
        Banner,
        ColoredWallBanner,
        StainedGlass,
        StainedGlassPane,
        Wool,
        ColoredShulkerBox,
        Bed,
    }
}
