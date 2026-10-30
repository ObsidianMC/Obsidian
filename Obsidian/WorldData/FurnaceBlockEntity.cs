using Obsidian.API.Containers;
using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.Nbt;
using Obsidian.Net;
using System.Runtime.CompilerServices;

namespace Obsidian.WorldData;

/// <summary>Cooking state, owned by the same lock as the furnace inventory.</summary>
internal sealed partial class FurnaceBlockEntity
{
    private static readonly ConditionalWeakTable<SmeltingContainer, FurnaceBlockEntity> machines = new();

    internal FurnaceBlockEntity(SmeltingContainer furnace)
        : base(furnace, "cooking_time_spent", "cooking_total_time",
            "lit_time_remaining", "lit_total_time", "RecipesUsed")
    {
        machines.Add(furnace, this);
    }

    internal static FurnaceBlockEntity? Of(SmeltingContainer furnace) =>
        machines.TryGetValue(furnace, out var machine) ? machine : null;

    internal int LitRemaining { get; private set; }
    internal int LitTotal { get; private set; }
    internal int CookingSpent { get; private set; }
    internal int CookingTotal { get; private set; } = 200;
    internal Dictionary<string, int> RecipesUsed { get; } = [];

    private ItemStack? previousInput;
    private string? previousRecipe;

    internal SmeltingContainer Furnace => (SmeltingContainer)this.Inventory;

    internal CraftingType RecipeType => this.Furnace.Type switch
    {
        InventoryType.Smoker => CraftingType.Smoking,
        InventoryType.BlastFurnace => CraftingType.Blasting,
        _ => CraftingType.Smelting
    };

    protected internal override void Load(NbtCompound tag)
    {
        base.Load(tag);
        this.CookingSpent = Read("cooking_time_spent");
        this.CookingTotal = Read("cooking_total_time");
        this.LitRemaining = Read("lit_time_remaining");
        this.LitTotal = Read("lit_total_time");
        this.previousInput = this.Inventory[IngredientSlot] is ItemStack input
            ? NetworkBuffer.SnapshotItemStack(input)
            : null;
        this.RecipesUsed.Clear();
        if (tag.TryGetTag<NbtCompound>("RecipesUsed", out var used))
        {
            foreach (var (name, count) in used)
            {
                if (count is NbtTag<int> value && value.Value > 0)
                    this.RecipesUsed[name] = value.Value;
            }
        }

        int Read(string name) => tag.TryGetTagValue<int>(name, out var value) ? Math.Max(0, value)
            : tag.TryGetTagValue<short>(name, out var legacy) ? Math.Max(0, (int)legacy) : 0;
    }

    internal override void SaveMachineState(NbtCompound tag)
    {
        tag.Add(new NbtTag<int>("cooking_time_spent", this.CookingSpent));
        tag.Add(new NbtTag<int>("cooking_total_time", this.CookingTotal));
        tag.Add(new NbtTag<int>("lit_time_remaining", this.LitRemaining));
        tag.Add(new NbtTag<int>("lit_total_time", this.LitTotal));
        var used = new NbtCompound("RecipesUsed");
        foreach (var (name, count) in this.RecipesUsed)
            used.Add(new NbtTag<int>(name, count));

        tag.Add(used);
    }

    /// <summary>Advances cooking once, under the inventory lock. Returns whether item slots changed.</summary>
    internal bool Cook(RecipeSet recipes)
    {
        var input = this.Inventory[IngredientSlot];
        var fuel = this.Inventory[FuelSlot];
        if (this.LitRemaining == 0 && this.CookingSpent == 0 && input.IsNullOrAir())
            return false;

        var before = (this.LitRemaining, this.LitTotal, this.CookingSpent, this.CookingTotal);
        var slots = false;
        var recipe = input.IsNullOrAir() ? null : recipes.FindCooking(this.RecipeType, input!, this.previousRecipe);
        this.previousRecipe = recipe?.Identifier ?? this.previousRecipe;
        if (!SameInput(this.previousInput, input))
        {
            this.CookingSpent = 0;
            this.CookingTotal = recipe?.CookingTime ?? 200;
            this.previousInput = input is not null ? NetworkBuffer.SnapshotItemStack(input) : null;
        }

        if (this.LitRemaining > 0)
            this.LitRemaining--;

        var canCook = CanCook(input, this.Inventory[ResultSlot], recipe);
        // Vanilla lets cooling progress decay by two only when there is neither a flame nor both input and fuel.
        if (this.LitRemaining > 0 || !fuel.IsNullOrAir() && !input.IsNullOrAir())
        {
            if (this.LitRemaining == 0 && canCook && fuel is not null)
            {
                var duration = FurnaceFuels.BurnTime(fuel);
                if (this.RecipeType != CraftingType.Smelting)
                    duration /= 2;

                this.LitRemaining = duration;
                this.LitTotal = duration;
                if (duration > 0)
                {
                    this.Inventory.RemoveItem(FuelSlot, 1);
                    if (fuel.Count <= 0 && fuel.Is(ItemsRegistry.LavaBucket))
                        this.Inventory[FuelSlot] = new ItemStack(ItemsRegistry.Bucket, 1);

                    slots = true;
                }
            }

            if (this.LitRemaining > 0 && canCook)
            {
                if (this.CookingTotal <= 0)
                    this.CookingTotal = recipe!.CookingTime;

                this.CookingSpent++;
                if (this.CookingSpent >= this.CookingTotal)
                {
                    this.CookingSpent = 0;
                    this.CookingTotal = recipe!.CookingTime;
                    var result = recipe.Result.First();
                    if (this.Inventory[ResultSlot].IsNullOrAir())
                        this.Inventory[ResultSlot] = NetworkBuffer.SnapshotItemStack(result);
                    else
                        this.Inventory[ResultSlot]!.Count += result.Count;

                    // Smelting a wet sponge with an empty bucket fills the bucket, even if another fuel is burning.
                    if (input!.Is(ItemsRegistry.WetSponge)
                        && this.Inventory[FuelSlot]?.Is(ItemsRegistry.Bucket) == true)
                    {
                        this.Inventory[FuelSlot] = new ItemStack(ItemsRegistry.WaterBucket, 1);
                    }

                    this.Inventory.RemoveItem(IngredientSlot, 1);
                    this.RecipesUsed[recipe.Identifier] = this.RecipesUsed.GetValueOrDefault(recipe.Identifier) + 1;
                    slots = true;
                }
            }
            else
            {
                this.CookingSpent = 0;
            }
        }
        else if (this.CookingSpent > 0)
        {
            this.CookingSpent = Math.Max(0, this.CookingSpent - 2);
        }

        if (slots || before != (this.LitRemaining, this.LitTotal, this.CookingSpent, this.CookingTotal))
            this.SetChanged();

        return slots;
    }

    private static bool SameInput(ItemStack? before, ItemStack? after) =>
        before.IsNullOrAir() && after.IsNullOrAir()
        || before is not null && after is not null && before == after;

    private static bool CanCook(ItemStack? input, ItemStack? output, SmeltingRecipe? recipe)
    {
        if (input.IsNullOrAir() || recipe is null || !recipe.Result.Any())
            return false;

        var result = recipe.Result.First();
        if (result.IsAir || result.Count <= 0)
            return false;

        return output.IsNullOrAir()
            ? result.Count <= result.MaxStackSize
            : output == result && output!.Count + result.Count <= Math.Min(output.MaxStackSize, result.MaxStackSize);
    }

    /// <summary>Takes accumulated XP on player extraction, including results previously taken by hoppers.</summary>
    internal int TakeExperience(RecipeSet recipes, Func<float> random)
    {
        var experience = 0;
        foreach (var (name, count) in this.RecipesUsed.ToArray())
        {
            // Keep an unloaded plugin's history; it can be awarded when that recipe returns.
            if (recipes.Recipes.GetValueOrDefault(name) is not SmeltingRecipe recipe)
                continue;

            var exact = count * recipe.Experience;
            var rounded = (int)MathF.Floor(exact);
            if (exact > rounded && random() < exact - rounded)
                rounded++;

            experience += rounded;
            this.RecipesUsed.Remove(name);
        }

        this.SetChanged();
        return experience;
    }

    protected internal override ValueTask TickAsync() =>
        this.Level is AbstractLevel level ? level.TickFurnaceAsync(this) : ValueTask.CompletedTask;
}
