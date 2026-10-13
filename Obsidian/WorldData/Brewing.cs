using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.Registries;

namespace Obsidian.WorldData;

/// <summary>
/// A brewing stand's tick, like vanilla's <c>BrewingStandBlockEntity.serverTick</c>: it takes fuel, starts a brew
/// when an ingredient brews one of its bottles, counts it down and mixes the bottles when it ends.
/// </summary>
internal static class Brewing
{
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
    /// Runs one tick of <paramref name="stand"/>. Take its lock first: players' clicks change the same slots.
    /// </summary>
    /// <remarks>
    /// The order of the steps, the brew time (400 ticks) and the 20 brews a fuel item lasts are vanilla's, since they
    /// decide when and what players see brew.
    /// </remarks>
    public static Step Tick(BrewingStand stand)
    {
        var slots = false;
        var properties = false;
        var brewed = false;
        ItemStack? remainder = null;

        var fuel = stand.GetItem(BrewingStand.FuelSlot);
        if (stand.Fuel <= 0 && fuel is not null && IsFuel(fuel))
        {
            stand.Fuel = BrewingRegistry.FuelUses;
            stand.RemoveItem(BrewingStand.FuelSlot, 1);
            slots = true;
            properties = true;
        }

        var canBrew = CanBrew(stand);
        var ingredient = stand.GetItem(BrewingStand.IngredientSlot);

        if (stand.BrewTime > 0)
        {
            stand.BrewTime--;
            properties = true;

            if (stand.BrewTime == 0 && canBrew)
            {
                remainder = Brew(stand);
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
            stand.Fuel--;
            stand.BrewTime = BrewingRegistry.BrewTime;
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
    /// Whether <paramref name="item"/> fuels brewing stands: vanilla's <c>#minecraft:brewing_fuel</c>, blaze powder.
    /// </summary>
    public static bool IsFuel(ItemStack item) => TagsRegistry.Item.BrewingFuel.Entries.Contains(item.Holder.Id);

    /// <summary>
    /// Whether the slot of a brewing stand takes <paramref name="item"/>, like vanilla's brewing menu's slots: bottles
    /// (and glass bottles) in the bottle slots, ingredients in the ingredient slot and fuel in the fuel slot.
    /// </summary>
    public static bool SlotAccepts(int slot, ItemStack item) => slot switch
    {
        BrewingStand.IngredientSlot => BrewingRegistry.IsIngredient(item),
        BrewingStand.FuelSlot => IsFuel(item),
        _ => IsBottle(item)
    };

    /// <summary>
    /// What a bottle slot takes: potions, splash and lingering potions, and glass bottles (vanilla's
    /// <c>PotionSlot.mayPlaceItem</c>). It holds one at a time.
    /// </summary>
    public static bool IsBottle(ItemStack item) =>
        item.Holder.Id is ItemIds.Potion or ItemIds.SplashPotion or ItemIds.LingeringPotion or ItemIds.GlassBottle;

    // Vanilla's isBrewable: the ingredient brews at least one of the bottles.
    private static bool CanBrew(BrewingStand stand)
    {
        var ingredient = stand.GetItem(BrewingStand.IngredientSlot);
        if (ingredient is null || ingredient.IsAir || !BrewingRegistry.IsIngredient(ingredient))
            return false;

        for (var slot = BrewingStand.FirstBottleSlot; slot < BrewingStand.FirstBottleSlot + 3; slot++)
        {
            if (stand.GetItem(slot) is ItemStack bottle && !bottle.IsAir && BrewingRegistry.HasMix(bottle, ingredient))
                return true;
        }

        return false;
    }

    // Vanilla's doBrew: mixes every bottle, then uses up one ingredient. What the ingredient leaves behind (a glass
    // bottle, for dragon's breath) takes its place once it's used up, and is dropped otherwise.
    private static ItemStack? Brew(BrewingStand stand)
    {
        var ingredient = stand.GetItem(BrewingStand.IngredientSlot)!;

        for (var slot = BrewingStand.FirstBottleSlot; slot < BrewingStand.FirstBottleSlot + 3; slot++)
        {
            if (stand.GetItem(slot) is ItemStack bottle)
            {
                var mixed = BrewingRegistry.Mix(ingredient, bottle);
                if (!ReferenceEquals(mixed, bottle))
                    stand.SetItem(slot, mixed);
            }
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
