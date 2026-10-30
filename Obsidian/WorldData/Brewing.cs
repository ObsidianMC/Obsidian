using Obsidian.API.Containers;
using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Registries;

namespace Obsidian.WorldData;

/// <summary>
/// A brewing stand's tick, like vanilla's <c>BrewingStandBlockEntity.serverTick</c>: it takes fuel, starts a brew
/// when an ingredient brews one of its bottles, counts it down and brews the bottles when it ends. What brews into
/// what comes from the brewing recipes (<c>minecraft:brewing</c>), then plugins' mixes (see
/// <see cref="PluginPotionMixes"/>).
/// </summary>
internal static class Brewing
{
    /// <summary>
    /// How long a brew takes at normal speed, in ticks (vanilla's <c>BrewingStandBlockEntity</c>).
    /// </summary>
    public const int BrewTime = 400;

    /// <summary>
    /// What a stand's tick changed, for the level to show.
    /// </summary>
    /// <param name="Slots">Whether its slots changed.</param>
    /// <param name="Properties">Whether its brew time or fuel changed, which its menu shows.</param>
    /// <param name="Brewed">Whether a brew ended, which plays vanilla's brewing sound.</param>
    /// <param name="Remainder">What the ingredient leaves behind that stays out of the stand, to drop.</param>
    /// <param name="Bottles">Which bottle slots hold something, when that changed, for the block's state.</param>
    internal readonly record struct Step(
        bool Slots,
        bool Properties,
        bool Brewed,
        ItemStack? Remainder,
        bool[]? Bottles);

    /// <summary>
    /// Runs one tick of <paramref name="stand"/>, brewing by <paramref name="recipes"/>. Take its lock first:
    /// players' clicks change the same slots.
    /// </summary>
    /// <remarks>
    /// The order of the steps is vanilla's, since it decides when and what players see brew. A fuel's brews and speed
    /// come from its <c>minecraft:brewing_fuel</c>, worked out for a default brewing stand block.
    /// </remarks>
    public static Step Tick(BrewingStand stand, RecipeSet recipes)
    {
        var slots = false;
        var properties = false;
        var brewed = false;
        ItemStack? remainder = null;

        // Out of fuel: burn the next fuel item for its brews and speed.
        var fuel = stand.GetItem(BrewingStand.FuelSlot);
        if (stand.Fuel <= 0 && fuel is not null && ItemFuels.BrewingFuel(fuel) is FuelComponentData fuelData)
        {
            var block = BlocksRegistry.BrewingStand;

            stand.Fuel = ComponentNumbers.Resolve(fuelData.Amount, block, Random.Shared);
            stand.TotalFuel = stand.Fuel;
            stand.SpeedMultiplier = ComponentNumbers.Resolve(fuelData.SpeedMultiplier, block, Random.Shared);
            stand.RemoveItem(BrewingStand.FuelSlot, 1);
            slots = true;
            properties = true;
        }

        var canBrew = CanBrew(stand, recipes);
        var ingredient = stand.GetItem(BrewingStand.IngredientSlot);

        if (stand.BrewTime > 0)
        {
            // A brew in progress: count it down, brewing at the end, and stop it when its ingredient goes.
            stand.BrewTime--;
            properties = true;

            if (stand.BrewTime == 0 && canBrew)
            {
                remainder = Brew(stand, recipes);
                slots = true;
                brewed = true;
            }
            else if (!canBrew || ingredient?.Holder != stand.BrewingIngredient)
            {
                stand.BrewTime = 0;
            }
        }
        else if (canBrew && stand.Fuel > 0)
        {
            // Start a brew, which uses one of the fuel's brews and takes as long as its speed says.
            stand.Fuel--;
            stand.BrewTime = TotalTime(stand.SpeedMultiplier);
            stand.TotalBrewTime = stand.BrewTime;
            stand.BrewingIngredient = ingredient!.Holder;
            properties = true;
        }

        var bottles = new bool[3];
        for (var slot = 0; slot < bottles.Length; slot++)
            bottles[slot] = stand.GetItem(BrewingStand.FirstBottleSlot + slot) is { IsAir: false };

        var shown = stand.ShownBottles;
        var bottlesChanged = shown is null || !shown.AsSpan().SequenceEqual(bottles);
        if (bottlesChanged)
            stand.ShownBottles = bottles;

        return new Step(slots, properties, brewed, remainder, bottlesChanged ? bottles : null);
    }

    /// <summary>
    /// Whether <paramref name="item"/> fuels brewing stands: whether it has a <c>minecraft:brewing_fuel</c>, by
    /// default (blaze powder) or set on the stack (vanilla's brewing menu's fuel slot).
    /// </summary>
    public static bool IsFuel(ItemStack item) => ItemFuels.BrewingFuel(item) is not null;

    /// <summary>
    /// Whether <paramref name="item"/> goes in a stand's ingredient slot: some brewing recipe's reagent (vanilla's
    /// <c>brewing_reagent</c> property set), or a plugin mix's ingredient.
    /// </summary>
    public static bool IsIngredient(ItemStack item, RecipeSet recipes) =>
        recipes.IsBrewingReagent(item) || PluginPotionMixes.IsIngredient(item);

    /// <summary>
    /// Whether the slot of a brewing stand takes <paramref name="item"/>, like vanilla's brewing menu's slots: bottles
    /// in the bottle slots, ingredients in the ingredient slot and fuel in the fuel slot.
    /// </summary>
    public static bool SlotAccepts(int slot, ItemStack item, RecipeSet recipes) => slot switch
    {
        BrewingStand.IngredientSlot => IsIngredient(item, recipes),
        BrewingStand.FuelSlot => IsFuel(item),
        _ => IsBottle(item, recipes)
    };

    /// <summary>
    /// What a bottle slot takes (vanilla's <c>PotionIngredient.isPotionInput</c>): some brewing recipe's input item,
    /// or an item tagged <c>minecraft:brewing_potion_inputs</c> (potions, splash and lingering potions, and glass
    /// bottles). It holds one at a time.
    /// </summary>
    public static bool IsBottle(ItemStack item, RecipeSet recipes) =>
        recipes.IsBrewingInput(item) || TagsRegistry.Item.BrewingPotionInputs.Entries.Contains(item.Holder.Id);

    /// <summary>
    /// What <paramref name="ingredient"/> brews <paramref name="bottle"/> into, or null when nothing does: the first
    /// brewing recipe that matches, else the first plugin mix.
    /// </summary>
    public static ItemStack? Mix(ItemStack bottle, ItemStack ingredient, RecipeSet recipes)
    {
        if (bottle.IsAir)
            return null;

        if (recipes.FindBrewing(bottle, ingredient) is BrewingRecipe recipe)
            return Net.NetworkBuffer.SnapshotItemStack(recipe.Output);

        return PluginPotionMixes.Mix(bottle, ingredient);
    }

    // Vanilla's brew time for a fuel's speed multiplier: 400 ticks at normal speed, rounded up, and normal speed for a
    // multiplier that isn't positive.
    private static int TotalTime(float speedMultiplier)
    {
        var speed = speedMultiplier > 0 ? speedMultiplier : 1f;
        return (int)Math.Ceiling(BrewTime / speed);
    }

    // Vanilla's isBrewable: the ingredient is a reagent and brews at least one of the bottles.
    private static bool CanBrew(BrewingStand stand, RecipeSet recipes)
    {
        var ingredient = stand.GetItem(BrewingStand.IngredientSlot);
        if (ingredient is null || ingredient.IsAir || !IsIngredient(ingredient, recipes))
            return false;

        for (var slot = BrewingStand.FirstBottleSlot; slot < BrewingStand.FirstBottleSlot + 3; slot++)
        {
            if (stand.GetItem(slot) is ItemStack bottle && Mix(bottle, ingredient, recipes) is not null)
                return true;
        }

        return false;
    }

    // Vanilla's doBrew: brews every bottle, then uses up one ingredient. What the ingredient leaves behind (a glass
    // bottle, for dragon's breath) takes its place once it's used up, and is dropped otherwise.
    private static ItemStack? Brew(BrewingStand stand, RecipeSet recipes)
    {
        var ingredient = stand.GetItem(BrewingStand.IngredientSlot)!;

        for (var slot = BrewingStand.FirstBottleSlot; slot < BrewingStand.FirstBottleSlot + 3; slot++)
        {
            if (stand.GetItem(slot) is ItemStack bottle && Mix(bottle, ingredient, recipes) is ItemStack brewed)
                stand.SetItem(slot, brewed);
        }

        var left = Remainder(ingredient.Holder);
        stand.RemoveItem(BrewingStand.IngredientSlot, 1);

        if (left is null)
            return null;

        if (stand.GetItem(BrewingStand.IngredientSlot) is null)
        {
            stand.SetItem(BrewingStand.IngredientSlot, left);
            return null;
        }

        return left;
    }

    // Vanilla's crafting remainders of brewing ingredients: only dragon's breath has one among vanilla's.
    private static ItemStack? Remainder(Item ingredient) =>
        ingredient.Id == ItemIds.DragonBreath ? new ItemStack(ItemsRegistry.GlassBottle, 1) : null;
}
