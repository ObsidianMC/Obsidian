using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Net;
using System.Collections.Frozen;

namespace Obsidian.Registries;

public static partial class RecipesRegistry
{
    // Copy every mutable part before publishing; caller-owned ingredients and stacks never enter the set.
    internal static IRecipe Snapshot(IRecipe recipe) => recipe switch
    {
        ShapedRecipe value => new ShapedRecipe
        {
            Identifier = value.Identifier,
            Type = value.Type,
            Group = value.Group,
            Category = value.Category,
            Result = Snapshot(value.Result),
            Pattern = Array.AsReadOnly(value.Pattern.ToArray()),
            Key = value.Key.ToFrozenDictionary(pair => pair.Key, pair => Snapshot(pair.Value)),
        },
        ShapelessRecipe value => new ShapelessRecipe
        {
            Identifier = value.Identifier,
            Type = value.Type,
            Group = value.Group,
            Category = value.Category,
            Result = Snapshot(value.Result),
            Ingredients = Array.AsReadOnly(value.Ingredients.Select(Snapshot).ToArray()),
        },
        SmeltingRecipe value => new SmeltingRecipe
        {
            Identifier = value.Identifier,
            Type = value.Type,
            Group = value.Group,
            Result = Snapshot(value.Result),
            Ingredient = Snapshot(value.Ingredient),
            Experience = value.Experience,
            CookingTime = value.CookingTime,
            Category = value.Category,
        },
        CuttingRecipe value => new CuttingRecipe
        {
            Identifier = value.Identifier,
            Type = value.Type,
            Group = value.Group,
            Result = Snapshot(value.Result),
            Ingredient = Snapshot(value.Ingredient),
            Count = value.Count,
        },
        SmithingTransformRecipe value => new SmithingTransformRecipe
        {
            Identifier = value.Identifier,
            Type = value.Type,
            Group = value.Group,
            Result = Snapshot(value.Result),
            Base = Snapshot(value.Base),
            Addition = Snapshot(value.Addition),
            Template = Snapshot(value.Template),
        },
        SmithingTrimRecipe value => new SmithingTrimRecipe
        {
            Identifier = value.Identifier,
            Type = value.Type,
            Group = value.Group,
            Base = Snapshot(value.Base),
            Addition = Snapshot(value.Addition),
            Template = Snapshot(value.Template),
            Pattern = value.Pattern,
        },
        IRecipeWithResult value => new StoredRecipeWithResult
        {
            Identifier = value.Identifier,
            Type = value.Type,
            Group = value.Group,
            Result = Snapshot(value.Result)
        },
        _ => new StoredRecipe
        {
            Identifier = recipe.Identifier,
            Type = recipe.Type,
            Group = recipe.Group
        }
    };

    private static Ingredient Snapshot(Ingredient ingredient)
    {
        var copy = new Ingredient();

        foreach (var item in ingredient)
            copy.Add(CopySavedComponents(NetworkBuffer.SnapshotItemStack(item)));

        return copy;
    }

    // Network snapshots preserve saved-only data by reference. Registration must also own that data, including
    // data on nested stacks, since a plugin may retain the NBT it originally loaded the result from.
    private static ItemStack CopySavedComponents(ItemStack copy)
    {
        // Components are immutable, so a nested stack's copy goes in as a new component, after the enumeration.
        var components = new List<DataComponent>();

        foreach (var component in copy)
            components.Add(component);

        foreach (var component in components)
        {
            if (component is SimpleDataComponent<ItemStack> { Value: ItemStack nested } single)
            {
                copy[single.Type] = single with { Value = CopySavedComponents(nested) };
            }
            else if (component is SimpleDataComponent<ImmutableArray<ItemStack>> { Value: var items } many
                && !items.IsDefaultOrEmpty)
            {
                copy[many.Type] = many with { Value = [.. items.Select(CopySavedComponents)] };
            }
        }

        if (copy.UnmodeledComponents is null)
            return copy;

        using var writer = new NetworkBuffer();
        writer.WriteNbtCompound(copy.UnmodeledComponents);
        using var reader = new NetworkBuffer(writer.AsSpan(0, writer.Size).ToArray());

        return new ItemStack(copy, copy.Count)
        {
            UnmodeledComponents = reader.ReadNbtCompound()
        };
    }

    private class StoredRecipe : IRecipe
    {
        public required string Identifier { get; init; }
        public CraftingType Type { get; init; }
        public string? Group { get; init; }
    }

    private sealed class StoredRecipeWithResult : StoredRecipe, IRecipeWithResult
    {
        public required Ingredient Result { get; init; }
    }
}
