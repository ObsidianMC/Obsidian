namespace Obsidian.WorldData.Structures;

/// <summary>
/// Parses block states written like <c>minecraft:oak_stairs[facing=east,half=bottom]</c>, as vanilla's
/// <c>BlockStateParser.parseForBlock</c> reads jigsaw <c>final_state</c> values.
/// </summary>
internal static class BlockStateParser
{
    /// <summary>
    /// The block state, or <c>null</c> when the block, a property or a value is unknown. Unlisted properties keep their
    /// defaults, and anything after the properties (block entity data, stray characters) is ignored like vanilla does.
    /// </summary>
    public static IBlock? TryParse(string text)
    {
        var bracket = text.IndexOf('[');
        var nameEnd = bracket >= 0 ? bracket : text.IndexOf('{');
        var name = (nameEnd >= 0 ? text[..nameEnd] : text).Trim();
        if (!name.Contains(':'))
            name = "minecraft:" + name;

        var properties = new Dictionary<string, string>();
        if (bracket >= 0)
        {
            var close = text.IndexOf(']', bracket);
            if (close < 0)
                return null;

            foreach (var entry in text[(bracket + 1)..close].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separator = entry.IndexOf('=');
                if (separator < 0)
                    return null;

                properties[entry[..separator].Trim()] = entry[(separator + 1)..].Trim();
            }
        }

        IBlock state;
        try
        {
            state = BlockStateProperties.GetState(name, properties);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        foreach (var (property, value) in properties)
        {
            if (state.GetProperty(property) != value)
                return null;
        }

        return state;
    }
}

/// <summary>
/// Block shape queries vanilla's structure code makes that the block physics data doesn't cover directly.
/// </summary>
internal static class StructureBlockShapes
{
    // Blocks whose outline shape is a full cube although their collision shape isn't (dumped from vanilla 1.21.11).
    private static readonly HashSet<string> fullShapeBlocks =
    [
        "minecraft:cobweb", "minecraft:soul_sand", "minecraft:sunflower", "minecraft:lilac", "minecraft:rose_bush", "minecraft:peony",
        "minecraft:tall_grass", "minecraft:large_fern", "minecraft:pitcher_plant", "minecraft:end_gateway", "minecraft:kelp_plant",
        "minecraft:honey_block", "minecraft:powder_snow", "minecraft:sculk_shrieker", "minecraft:mud", "minecraft:firefly_bush"
    ];

    // Multiface blocks without any face fall back to a full outline shape.
    private static readonly HashSet<string> facelessFullShapeBlocks =
        ["minecraft:vine", "minecraft:glow_lichen", "minecraft:resin_clump", "minecraft:sculk_vein"];

    /// <summary>
    /// Vanilla <c>Block.isShapeFullBlock(state.getShape(...))</c>: the outline shape is a full cube.
    /// </summary>
    public static bool IsShapeFullBlock(IBlock block)
    {
        if (block.IsCollisionShapeFullBlock() || fullShapeBlocks.Contains(block.UnlocalizedName))
            return true;

        return block.UnlocalizedName switch
        {
            "minecraft:wheat" => block.GetProperty("age") == "7",
            "minecraft:snow" => block.GetProperty("layers") == "8",
            "minecraft:sweet_berry_bush" => block.GetProperty("age") == "3",
            "minecraft:pale_moss_carpet" => block.GetProperty("bottom") == "false"
                && block.GetProperty("north") == "none" && block.GetProperty("east") == "none"
                && block.GetProperty("south") == "none" && block.GetProperty("west") == "none",
            _ => facelessFullShapeBlocks.Contains(block.UnlocalizedName) && !HasAnyFace(block)
        };
    }

    private static bool HasAnyFace(IBlock block)
    {
        foreach (var face in (ReadOnlySpan<string>)["up", "down", "north", "east", "south", "west"])
        {
            if (block.GetProperty(face) == "true")
                return true;
        }

        return false;
    }
}
