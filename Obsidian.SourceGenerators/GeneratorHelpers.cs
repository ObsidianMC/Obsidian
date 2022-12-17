using System.Text.RegularExpressions;

namespace Obsidian.SourceGenerators;
internal static class GeneratorHelpers
{
    public static readonly Regex colorRegex = new("^(White|Orange|Magenta|LightBlue|Yellow|Lime|Pink|Gray|LightGray|Cyan|Purple|Blue|Brown|Green|Red|Black)");

    public static readonly HashSet<string> ignored = new()
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

    public static readonly HashSet<string> filters = new()
    {
        "Candle",
        "CandleCake",
        "Terracotta",
        "ShulkerBox",
        "WallBanner",
        "Banner"
    };
}
