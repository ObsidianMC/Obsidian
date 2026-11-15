namespace Obsidian.API.Crafting;

/// <summary>
/// The kinds of recipe (<see cref="IRecipe.Type"/>), named after vanilla's recipe serializers
/// (<c>minecraft:recipe_serializer</c>), not its recipe types (<c>minecraft:recipe_type</c>). It includes legacy
/// serializers that the current registry no longer has.
/// </summary>
public enum CraftingType
{
    /// <summary>
    /// A crafting grid recipe with a fixed shape (<c>minecraft:crafting_shaped</c>, <see cref="ShapedRecipe"/>).
    /// </summary>
    CraftingShaped,
    /// <summary>
    /// A crafting grid recipe whose items can go anywhere (<c>minecraft:crafting_shapeless</c>,
    /// <see cref="ShapelessRecipe"/>).
    /// </summary>
    CraftingShapeless,

    /// <summary>
    /// Vanilla's former built-in recipe dyeing leather armour (<c>minecraft:crafting_special_armordye</c>).
    /// Legacy: the current recipe serializer registry has no such serializer, and the server doesn't use it.
    /// </summary>
    CraftingSpecialArmordye,
    /// <summary>
    /// Vanilla's built-in recipe copying written books (<c>minecraft:crafting_special_bookcloning</c>).
    /// </summary>
    CraftingSpecialBookcloning,
    /// <summary>
    /// Vanilla's former built-in recipe copying filled maps (<c>minecraft:crafting_special_mapcloning</c>).
    /// Legacy: the current recipe serializer registry has no such serializer, and the server doesn't use it.
    /// </summary>
    CraftingSpecialMapcloning,
    /// <summary>
    /// Vanilla's built-in recipe zooming out filled maps (<c>minecraft:crafting_special_mapextending</c>).
    /// </summary>
    CraftingSpecialMapextending,
    /// <summary>
    /// Vanilla's built-in firework rocket recipe (<c>minecraft:crafting_special_firework_rocket</c>).
    /// </summary>
    CraftingSpecialFireworkRocket,
    /// <summary>
    /// Vanilla's built-in firework star recipe (<c>minecraft:crafting_special_firework_star</c>).
    /// </summary>
    CraftingSpecialFireworkStar,
    /// <summary>
    /// Vanilla's built-in recipe adding a fade color to a firework star
    /// (<c>minecraft:crafting_special_firework_star_fade</c>).
    /// </summary>
    CraftingSpecialFireworkStarFade,
    /// <summary>
    /// Vanilla's former built-in tipped arrow recipe (<c>minecraft:crafting_special_tippedarrow</c>).
    /// Legacy: the current recipe serializer registry has no such serializer, and the server doesn't use it.
    /// </summary>
    CraftingSpecialTippedarrow,
    /// <summary>
    /// Vanilla's built-in recipe copying a banner's patterns (<c>minecraft:crafting_special_bannerduplicate</c>).
    /// </summary>
    CraftingSpecialBannerduplicate,
    /// <summary>
    /// Vanilla's built-in recipe putting a banner on a shield (<c>minecraft:crafting_special_shielddecoration</c>).
    /// </summary>
    CraftingSpecialShielddecoration,
    /// <summary>
    /// Vanilla's former built-in shulker box dyeing recipe (<c>minecraft:crafting_special_shulkerboxcoloring</c>).
    /// Legacy: the current recipe serializer registry has no such serializer, and the server doesn't use it.
    /// </summary>
    CraftingSpecialShulkerboxcoloring,
    /// <summary>
    /// Vanilla's former built-in suspicious stew recipe (<c>minecraft:crafting_special_suspiciousstew</c>).
    /// Legacy: the current recipe serializer registry has no such serializer, and the server doesn't use it.
    /// </summary>
    CraftingSpecialSuspiciousstew,
    /// <summary>
    /// Vanilla's built-in recipe combining two damaged items of the same kind
    /// (<c>minecraft:crafting_special_repairitem</c>).
    /// </summary>
    CraftingSpecialRepairitem,
    /// <summary>
    /// Vanilla's decorated pot recipe (<c>minecraft:crafting_decorated_pot</c>).
    /// </summary>
    CraftingDecoratedPot,

    /// <summary>
    /// A furnace recipe (<c>minecraft:smelting</c>, <see cref="SmeltingRecipe"/>).
    /// </summary>
    Smelting,
    /// <summary>
    /// A blast furnace recipe (<c>minecraft:blasting</c>, <see cref="SmeltingRecipe"/>).
    /// </summary>
    Blasting,
    /// <summary>
    /// A smoker recipe (<c>minecraft:smoking</c>, <see cref="SmeltingRecipe"/>).
    /// </summary>
    Smoking,
    /// <summary>
    /// A campfire recipe (<c>minecraft:campfire_cooking</c>, <see cref="SmeltingRecipe"/>).
    /// </summary>
    CampfireCooking,

    /// <summary>
    /// A stonecutter recipe (<c>minecraft:stonecutting</c>, <see cref="CuttingRecipe"/>).
    /// </summary>
    Stonecutting,

    /// <summary>
    /// A smithing table recipe turning one item into another (<c>minecraft:smithing_transform</c>,
    /// <see cref="SmithingTransformRecipe"/>).
    /// </summary>
    SmithingTransform,
    /// <summary>
    /// A smithing table recipe adding an armour trim (<c>minecraft:smithing_trim</c>,
    /// <see cref="SmithingTrimRecipe"/>).
    /// </summary>
    SmithingTrim,

    /// <summary>
    /// A crafting grid recipe turning an item into another while keeping its components
    /// (<c>minecraft:crafting_transmute</c>, <see cref="TransmuteRecipe"/>).
    /// </summary>
    CraftingTransmute,

    /// <summary>
    /// A brewing stand recipe (<c>minecraft:brewing</c>, <see cref="BrewingRecipe"/>).
    /// </summary>
    Brewing,

    // 26.1's dye and imbue recipes come after the existing members so that those keep their values, which compiled
    // plugins compare against.
    /// <summary>
    /// A crafting grid recipe dyeing an item (<c>minecraft:crafting_dye</c>, <see cref="DyeRecipe"/>).
    /// </summary>
    CraftingDye,
    /// <summary>
    /// A crafting grid recipe imbuing items with a potion (<c>minecraft:crafting_imbue</c>, <see cref="ImbueRecipe"/>).
    /// </summary>
    CraftingImbue
}
