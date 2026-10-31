using Obsidian.API.Containers;
using Obsidian.API.Crafting;
using Obsidian.Registries;
using System;
using Obsidian.Tests.Utilities;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// The recipe API that compiled plugins are built against stays binary compatible: an enum member's value is
/// compiled into the plugin, so a member that moves would make the plugin compare against another member, and a
/// method whose signature changes is missing when the plugin calls it.
/// </summary>
public sealed class RecipeApiCompatibility
{
    [Fact(DisplayName = "Crafting types keep the values plugins were compiled against")]
    public void CraftingTypesKeepTheirValues()
    {
        (CraftingType Type, int Value)[] cases =
        [
            (CraftingType.CraftingShaped, 0),
            (CraftingType.CraftingShapeless, 1),
            (CraftingType.CraftingSpecialArmordye, 2),
            (CraftingType.CraftingSpecialBookcloning, 3),
            (CraftingType.CraftingSpecialMapcloning, 4),
            (CraftingType.CraftingSpecialMapextending, 5),
            (CraftingType.CraftingSpecialFireworkRocket, 6),
            (CraftingType.CraftingSpecialFireworkStar, 7),
            (CraftingType.CraftingSpecialFireworkStarFade, 8),
            (CraftingType.CraftingSpecialTippedarrow, 9),
            (CraftingType.CraftingSpecialBannerduplicate, 10),
            (CraftingType.CraftingSpecialShielddecoration, 11),
            (CraftingType.CraftingSpecialShulkerboxcoloring, 12),
            (CraftingType.CraftingSpecialSuspiciousstew, 13),
            (CraftingType.CraftingSpecialRepairitem, 14),
            (CraftingType.CraftingDecoratedPot, 15),
            (CraftingType.Smelting, 16),
            (CraftingType.Blasting, 17),
            (CraftingType.Smoking, 18),
            (CraftingType.CampfireCooking, 19),
            (CraftingType.Stonecutting, 20),
            (CraftingType.SmithingTransform, 21),
            (CraftingType.SmithingTrim, 22),
            (CraftingType.CraftingTransmute, 23),
            (CraftingType.Brewing, 24)
        ];

        FixtureCases.AssertAll(
            cases,
            row => $"type={row.Type}, value={row.Value}",
            row => AssertCraftingTypesKeepTheirValues(row.Type, row.Value));
    }

    private static void AssertCraftingTypesKeepTheirValues(CraftingType type, int value)
    {
        Assert.Equal(value, (int)type);
    }

    [Fact(DisplayName = "RecipesRegistry.FindRecipe keeps the signature plugins were compiled against")]
    public void FindRecipeKeepsItsSignature()
    {
        Type[] parameters = [typeof(CraftingTable)];

        var findRecipe = typeof(RecipesRegistry).GetMethod("FindRecipe", parameters);

        Assert.NotNull(findRecipe);
        Assert.True(findRecipe.IsPublic && findRecipe.IsStatic);
        Assert.Equal(typeof(IRecipeWithResult), findRecipe.ReturnType);
    }
}
