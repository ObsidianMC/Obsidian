namespace Obsidian.API;

public class MinecraftArgumentTypes
{
    // TODO maybe make a Dictionary mapping an enum to a string?
    private static string[] mcTypes =
    {
            "brigadier:bool",
            "brigadier:double",
            "brigadier:float",
            "brigadier:integer",
            "brigadier:long",
            "brigadier:string",
            "minecraft:entity",
            "minecraft:game_profile",
            "minecraft:block_pos",
            "minecraft:column_pos",
            "minecraft:vec3",
            "minecraft:vec2",
            "minecraft:block_state",
            "minecraft:block_predicate",
            "minecraft:item_stack",
            "minecraft:item_predicate",
            "minecraft:color",
            "minecraft:component",
            "minecraft:message",
            "minecraft:nbt",
            "minecraft:nbt_path",
            "minecraft:objective",
            "minecraft:objective_criteria",
            "minecraft:operation",
            "minecraft:particle",
            "minecraft:rotation",
            "minecraft:scoreboard_slot",
            "minecraft:score_holder",
            "minecraft:swizzle",
            "minecraft:team",
            "minecraft:item_slot",
            "minecraft:resource_location",
            "minecraft:mob_effect",
            "minecraft:function",
            "minecraft:entity_anchor",
            "minecraft:range",
            "minecraft:int_range",
            "minecraft:float_range",
            "minecraft:item_enchantment",
            "minecraft:entity_summon",
            "minecraft:dimension",
            "minecraft:uuid",
            "minecraft:nbt_tag",
            "minecraft:nbt_compount_tag",
            "minecraft:time",
            // OBSIDIAN TYPES
            "obsidian:player"
        };

    public static bool IsValidMcType(string? input) => mcTypes.Contains(input);
}
