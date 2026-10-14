using Obsidian.API.Crafting;
using Obsidian.API.Containers;
using Obsidian.API.Inventory;

namespace Obsidian.WorldData.Furnaces;

/// <summary>
/// Vanilla furnace rules: fuel burn times from <c>FuelValues</c> and smelting recipe lookup for furnace, blast furnace and smoker.
/// </summary>
internal static class FurnaceRules
{
    /// <summary>The vanilla default cook time, used when no recipe matches.</summary>
    public const int DefaultCookTime = 200;

    private enum FuelKind { Item, Tag, Exclude }

    private readonly record struct FuelEntry(FuelKind Kind, string? Tag, Material Material, int BurnTime);

    // Ordered like vanilla's FuelValues builder: a later entry overrides an earlier one, and Exclude removes its tag's items.
    private static readonly FuelEntry[] fuelEntries =
    [
        new(FuelKind.Item, null, Material.LavaBucket, 20000),
        new(FuelKind.Item, null, Material.CoalBlock, 16000),
        new(FuelKind.Item, null, Material.BlazeRod, 2400),
        new(FuelKind.Item, null, Material.Coal, 1600),
        new(FuelKind.Item, null, Material.Charcoal, 1600),
        new(FuelKind.Tag, "logs", default, 300),
        new(FuelKind.Tag, "bamboo_blocks", default, 300),
        new(FuelKind.Tag, "planks", default, 300),
        new(FuelKind.Item, null, Material.BambooMosaic, 300),
        new(FuelKind.Tag, "wooden_stairs", default, 300),
        new(FuelKind.Item, null, Material.BambooMosaicStairs, 300),
        new(FuelKind.Tag, "wooden_slabs", default, 150),
        new(FuelKind.Item, null, Material.BambooMosaicSlab, 150),
        new(FuelKind.Tag, "wooden_trapdoors", default, 300),
        new(FuelKind.Tag, "wooden_pressure_plates", default, 300),
        new(FuelKind.Tag, "wooden_shelves", default, 300),
        new(FuelKind.Tag, "wooden_fences", default, 300),
        new(FuelKind.Tag, "fence_gates", default, 300),
        new(FuelKind.Item, null, Material.NoteBlock, 300),
        new(FuelKind.Item, null, Material.Bookshelf, 300),
        new(FuelKind.Item, null, Material.ChiseledBookshelf, 300),
        new(FuelKind.Item, null, Material.Lectern, 300),
        new(FuelKind.Item, null, Material.Jukebox, 300),
        new(FuelKind.Item, null, Material.Chest, 300),
        new(FuelKind.Item, null, Material.TrappedChest, 300),
        new(FuelKind.Item, null, Material.CraftingTable, 300),
        new(FuelKind.Item, null, Material.DaylightDetector, 300),
        new(FuelKind.Tag, "banners", default, 300),
        new(FuelKind.Item, null, Material.Bow, 300),
        new(FuelKind.Item, null, Material.FishingRod, 300),
        new(FuelKind.Item, null, Material.Ladder, 300),
        new(FuelKind.Tag, "signs", default, 200),
        new(FuelKind.Tag, "hanging_signs", default, 800),
        new(FuelKind.Item, null, Material.WoodenShovel, 200),
        new(FuelKind.Item, null, Material.WoodenSword, 200),
        new(FuelKind.Item, null, Material.WoodenSpear, 200),
        new(FuelKind.Item, null, Material.WoodenHoe, 200),
        new(FuelKind.Item, null, Material.WoodenAxe, 200),
        new(FuelKind.Item, null, Material.WoodenPickaxe, 200),
        new(FuelKind.Tag, "wooden_doors", default, 200),
        new(FuelKind.Tag, "boats", default, 1200),
        new(FuelKind.Tag, "wool", default, 100),
        new(FuelKind.Tag, "wooden_buttons", default, 100),
        new(FuelKind.Item, null, Material.Stick, 100),
        new(FuelKind.Tag, "saplings", default, 100),
        new(FuelKind.Item, null, Material.Bowl, 100),
        new(FuelKind.Tag, "wool_carpets", default, 67),
        new(FuelKind.Item, null, Material.DriedKelpBlock, 4001),
        new(FuelKind.Item, null, Material.Crossbow, 300),
        new(FuelKind.Item, null, Material.Bamboo, 50),
        new(FuelKind.Item, null, Material.DeadBush, 100),
        new(FuelKind.Item, null, Material.ShortDryGrass, 100),
        new(FuelKind.Item, null, Material.TallDryGrass, 100),
        new(FuelKind.Item, null, Material.Scaffolding, 50),
        new(FuelKind.Item, null, Material.Loom, 300),
        new(FuelKind.Item, null, Material.Barrel, 300),
        new(FuelKind.Item, null, Material.CartographyTable, 300),
        new(FuelKind.Item, null, Material.FletchingTable, 300),
        new(FuelKind.Item, null, Material.SmithingTable, 300),
        new(FuelKind.Item, null, Material.Composter, 300),
        new(FuelKind.Item, null, Material.Azalea, 100),
        new(FuelKind.Item, null, Material.FloweringAzalea, 100),
        new(FuelKind.Item, null, Material.MangroveRoots, 300),
        new(FuelKind.Item, null, Material.LeafLitter, 100),
        new(FuelKind.Exclude, "non_flammable_wood", default, 0),
    ];

    /// <summary>Ticks one unit of <paramref name="fuel"/> keeps a furnace lit, or 0 when it isn't fuel.</summary>
    public static int GetBurnTime(ItemStack? fuel)
    {
        if (fuel is null || fuel.IsAir || fuel.Count <= 0)
            return 0;

        var burnTime = 0;
        foreach (var entry in fuelEntries)
        {
            if (!Matches(entry, fuel))
                continue;

            burnTime = entry.Kind == FuelKind.Exclude ? 0 : entry.BurnTime;
        }

        return burnTime;
    }

    /// <summary>The crafting type a furnace of <paramref name="type"/> smelts.</summary>
    public static CraftingType KindOf(InventoryType type) => type switch
    {
        InventoryType.BlastFurnace => CraftingType.Blasting,
        InventoryType.Smoker => CraftingType.Smoking,
        _ => CraftingType.Smelting,
    };

    /// <summary>True when <paramref name="item"/> can be used as furnace fuel.</summary>
    public static bool IsFuel(ItemStack? item) => GetBurnTime(item) > 0;

    /// <summary>The smelting recipe of <paramref name="kind"/> whose ingredient is <paramref name="input"/>, if any.</summary>
    public static SmeltingRecipe? FindRecipe(CraftingType kind, ItemStack? input)
    {
        if (input is null || input.IsAir)
            return null;

        foreach (var recipe in RecipesRegistry.SmeltingRecipes)
        {
            if (recipe.Type == kind && recipe.Ingredient.Any(ingredient => ingredient.Type == input.Type))
                return recipe;
        }

        return null;
    }

    private static bool Matches(FuelEntry entry, ItemStack item) => entry.Kind switch
    {
        FuelKind.Item => item.Type == entry.Material,
        _ => TagContains(entry.Tag!, item.Holder.Id),
    };

    private static bool TagContains(string tag, int itemId)
    {
        foreach (var candidate in TagsRegistry.Item.All)
        {
            if (string.Equals(candidate.Name, tag, StringComparison.OrdinalIgnoreCase))
                return candidate.Entries.Contains(itemId);
        }

        return false;
    }
}
