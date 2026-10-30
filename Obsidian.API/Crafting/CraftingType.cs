namespace Obsidian.API.Crafting;

public enum CraftingType
{
    CraftingShaped,
    CraftingShapeless,

    CraftingSpecialArmordye,
    CraftingSpecialBookcloning,
    CraftingSpecialMapcloning,
    CraftingSpecialMapextending,
    CraftingSpecialFireworkRocket,
    CraftingSpecialFireworkStar,
    CraftingSpecialFireworkStarFade,
    CraftingSpecialTippedarrow,
    CraftingSpecialBannerduplicate,
    CraftingSpecialShielddecoration,
    CraftingSpecialShulkerboxcoloring,
    CraftingSpecialSuspiciousstew,
    CraftingSpecialRepairitem,
    CraftingDecoratedPot,

    Smelting,
    Blasting,
    Smoking,
    CampfireCooking,

    Stonecutting,

    SmithingTransform,
    SmithingTrim,

    CraftingTransmute,

    Brewing,

    // 26.1's dye and imbue recipes come after the existing members so that those keep their values, which compiled
    // plugins compare against.
    CraftingDye,
    CraftingImbue
}
