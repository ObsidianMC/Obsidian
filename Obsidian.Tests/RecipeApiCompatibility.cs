using Obsidian.API.Containers;
using Obsidian.API.Crafting;
using Obsidian.Registries;
using System;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// The recipe API that compiled plugins are built against stays binary compatible: an enum member's value is
/// compiled into the plugin, so a member that moves would make the plugin compare against another member, and a
/// method whose signature changes is missing when the plugin calls it.
/// </summary>
public sealed class RecipeApiCompatibility
{
    [Theory(DisplayName = "Crafting types keep the values plugins were compiled against")]
    [InlineData(CraftingType.CraftingShaped, 0)]
    [InlineData(CraftingType.CraftingShapeless, 1)]
    [InlineData(CraftingType.CraftingSpecialArmordye, 2)]
    [InlineData(CraftingType.CraftingSpecialBookcloning, 3)]
    [InlineData(CraftingType.CraftingSpecialMapcloning, 4)]
    [InlineData(CraftingType.CraftingSpecialMapextending, 5)]
    [InlineData(CraftingType.CraftingSpecialFireworkRocket, 6)]
    [InlineData(CraftingType.CraftingSpecialFireworkStar, 7)]
    [InlineData(CraftingType.CraftingSpecialFireworkStarFade, 8)]
    [InlineData(CraftingType.CraftingSpecialTippedarrow, 9)]
    [InlineData(CraftingType.CraftingSpecialBannerduplicate, 10)]
    [InlineData(CraftingType.CraftingSpecialShielddecoration, 11)]
    [InlineData(CraftingType.CraftingSpecialShulkerboxcoloring, 12)]
    [InlineData(CraftingType.CraftingSpecialSuspiciousstew, 13)]
    [InlineData(CraftingType.CraftingSpecialRepairitem, 14)]
    [InlineData(CraftingType.CraftingDecoratedPot, 15)]
    [InlineData(CraftingType.Smelting, 16)]
    [InlineData(CraftingType.Blasting, 17)]
    [InlineData(CraftingType.Smoking, 18)]
    [InlineData(CraftingType.CampfireCooking, 19)]
    [InlineData(CraftingType.Stonecutting, 20)]
    [InlineData(CraftingType.SmithingTransform, 21)]
    [InlineData(CraftingType.SmithingTrim, 22)]
    [InlineData(CraftingType.CraftingTransmute, 23)]
    [InlineData(CraftingType.Brewing, 24)]
    public void CraftingTypesKeepTheirValues(CraftingType type, int value)
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
