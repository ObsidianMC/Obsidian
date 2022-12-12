using Obsidian.SourceGenerators.Registry.Models;
using System.Reflection.Metadata;
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
    private static void GenerateBlocks(Block[] blocks, GeneratorExecutionContext ctx)
    {
        var buttons = new Dictionary<string, Block>();
        var coloredBlocks = new Dictionary<BlockType, ColoredBlock>();
        var colors = new HashSet<string>();

        foreach (var block in blocks)
        {
            var blockName = block.Name;

            var match = colorRegex.Match(blockName);

            if (blockName.EndsWith("Button"))
            {
                buttons.Add(blockName.Replace("Button", string.Empty), block);
                continue;
            }
            else if (match.Success && !ignored.Contains(blockName))
            {
                var color = match.Value;

                colors.Add(color);

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

            var builder = new CodeBuilder()
                .Namespace("Obsidian.API.Blocks")
                .Line()
                .Type($"public sealed class {blockName}Block : IBlock, IPaletteValue<{blockName}Block>");

            builder.Indent().Line($"public static string UnlocalizedName => \"{block.Tag}\";");
            builder.Indent().Line($"public static int BaseId => {block.BaseId};");
            builder.Indent().Line("public int StateId { get; private set; }");
            builder.Indent().Line($"public Material Material => Material.{blockName};");

            builder.Indent().Line($"internal {blockName}Block()").Append("{}");

            foreach (var property in block.Properties)
                builder.Indent().Append($"public {property.Type} {property.Name} ").Append("{ get; private set; }").Line();

            builder.Line().Line($"public static {blockName}Block Construct(int state) => new();");

            builder.EndScope();
            ctx.AddSource($"{blockName}Block.g.cs", builder.ToString());
        }

        CreateButtons(buttons, ctx);
        CreateColoredBlocks(coloredBlocks, colors, ctx);
    }

    private static void CreateColoredBlocks(Dictionary<BlockType, ColoredBlock> blocks, HashSet<string> colors, GeneratorExecutionContext ctx)
    {
        var coloredBlockBuilder = new CodeBuilder()
             .Namespace("Obsidian.API")
             .Line()
             .Type($"public enum BlockColor");

        foreach (var color in colors)
            coloredBlockBuilder.Line($"{color},");

        coloredBlockBuilder.EndScope();

        ctx.AddSource("BlockColor.g.cs", coloredBlockBuilder.ToString());

        foreach (var coloredBlock in blocks.Values)
        {
            var type = coloredBlock.Type;
            var block = coloredBlock.Block;

            var blockBuilder = new CodeBuilder()
               .Namespace("Obsidian.API.Blocks")
               .Line()
               .Type($"public sealed class {type}Block : IBlock");

            blockBuilder.Indent().Line("public static string UnlocalizedName { get; private set; }");
            blockBuilder.Indent().Line("public static int BaseId { get; private set; }");
            blockBuilder.Indent().Line("public int StateId { get; private set; }");
            blockBuilder.Indent().Line("public BlockColor Color { get; private set; }");
            blockBuilder.Indent().Line($"public Material Material => Material.{type};");

            blockBuilder.Indent().Line($"internal {type}Block()").Append("{}");

            foreach (var property in block.Properties)
                blockBuilder.Indent().Append($"public {property.Type} {property.Name} ").Append("{ get; private set; }").Line();

            blockBuilder.Line().Line($"public static {type}Block Construct(int state) => new();");

            blockBuilder.EndScope();
            ctx.AddSource($"{type}Block.g.cs", blockBuilder.ToString());
        }
    }

    private static void CreateButtons(Dictionary<string, Block> buttons, GeneratorExecutionContext ctx)
    {
        var buttonTypeBuilder = new CodeBuilder()
              .Namespace("Obsidian.API.Blocks")
              .Line()
              .Type($"public enum ButtonType");

        foreach (var buttonType in buttons.Keys)
            buttonTypeBuilder.Line($"{buttonType},");

        buttonTypeBuilder.EndScope();

        ctx.AddSource("ButtonType.g.cs", buttonTypeBuilder.ToString());

        var button = buttons.First();

        var buttonBlock = button.Value;

        var buttonBuilder = new CodeBuilder()
               .Namespace("Obsidian.API.Blocks")
               .Line()
               .Type($"public sealed class ButtonBlock : IBlock");

        buttonBuilder.Indent().Line("public static string UnlocalizedName { get; private set; }");
        buttonBuilder.Indent().Line("public static int BaseId { get; private set; }");
        buttonBuilder.Indent().Line("public int StateId { get; private set; }");
        buttonBuilder.Indent().Line("public ButtonType Type { get; private set; }");
        buttonBuilder.Indent().Line("public Material Material => Material.Button;");

        buttonBuilder.Indent().Line("internal ButtonBlock(){}");

        foreach (var property in buttonBlock.Properties)
            buttonBuilder.Indent().Append($"public {property.Type} {property.Name} ").Append("{ get; private set; }").Line();

        buttonBuilder.Line().Line("public static ButtonBlock Construct(int state) => new();");

        buttonBuilder.EndScope();
        ctx.AddSource("ButtonBlock.g.cs", buttonBuilder.ToString());
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
