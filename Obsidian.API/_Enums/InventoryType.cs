namespace Obsidian.API;

/// <summary>The kind of screen a container opens as on the client.</summary>
public enum InventoryType
{
    /// <summary>An anvil.</summary>
    Anvil,

    /// <summary>A beacon.</summary>
    Beacon,

    /// <summary>A blast furnace.</summary>
    BlastFurnace,

    /// <summary>A brewing stand.</summary>
    BrewingStand,

    /// <summary>A cartography table.</summary>
    CartographyTable,

    /// <summary>A crafter.</summary>
    Crafter,

    /// <summary>A crafting table.</summary>
    Crafting,

    /// <summary>A container with no matching vanilla screen, such as one a plugin builds.</summary>
    Custom,

    /// <summary>An enchanting table.</summary>
    Enchantment,

    /// <summary>A furnace.</summary>
    Furnace,

    /// <summary>
    /// A chest-style grid of rows of 9 slots. A 5-slot container opens as a hopper screen instead, and a 9-slot
    /// dispenser or dropper container as a 3x3 grid.
    /// </summary>
    Generic,

    /// <summary>A grindstone.</summary>
    Grindstone,

    /// <summary>A hopper.</summary>
    Hopper,

    /// <summary>A lectern.</summary>
    Lectern,

    /// <summary>A loom.</summary>
    Loom,

    /// <summary>A villager or wandering trader's trade screen.</summary>
    Merchant,

    /// <summary>A shulker box.</summary>
    ShulkerBox,

    /// <summary>A smoker.</summary>
    Smoker,

    /// <summary>A stonecutter.</summary>
    Stonecutter,

    /// <summary>A smithing table.</summary>
    Smithing,
}
