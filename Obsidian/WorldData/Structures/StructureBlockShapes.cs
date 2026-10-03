namespace Obsidian.WorldData.Structures;

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
