using Obsidian.API.Crafting;
using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.WorldData.Furnaces;

/// <summary>
/// Vanilla furnace rules: fuel burn times from <c>FuelValues</c> and smelting recipe lookup for furnace, blast furnace and smoker.
/// </summary>
internal static class FurnaceRules
{
    /// <summary>The vanilla default cook time, used when no recipe matches.</summary>
    public const int DefaultCookTime = 200;

    // Vanilla's FuelValues, dumped from the server's builder (see VanillaDumper.dumpFuelValues): item resource name to burn time.
    private static readonly Dictionary<string, int> fuelBurnTimes = LoadFuelBurnTimes();

    private static Dictionary<string, int> LoadFuelBurnTimes()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Assets.fuel_values.json")
            ?? throw new InvalidDataException("Missing furnace fuel data.");
        return JsonSerializer.Deserialize<Dictionary<string, int>>(stream)!;
    }

    /// <summary>Ticks one unit of <paramref name="fuel"/> keeps a furnace lit, or 0 when it isn't fuel.</summary>
    public static int GetBurnTime(ItemStack? fuel)
    {
        if (fuel is null || fuel.IsAir || fuel.Count <= 0)
            return 0;

        return fuelBurnTimes.GetValueOrDefault(fuel.Holder.UnlocalizedName, 0);
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
}
