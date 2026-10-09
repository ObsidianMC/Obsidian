using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Xunit;

namespace Obsidian.Tests;

public sealed class ItemStackEquality
{
    [Fact]
    public void ComponentsAddedInAnyOrderAreEqual()
    {
        var first = new ItemStack(ItemsRegistry.DiamondSword, 1);
        first[DataComponentType.Damage] = ComponentBuilder.Damage with { Value = 5 };
        first[DataComponentType.EnchantmentGlintOverride] = ComponentBuilder.EnchantmentGlintOverride with { Value = true };
        var second = new ItemStack(ItemsRegistry.DiamondSword, 1);
        second[DataComponentType.EnchantmentGlintOverride] = ComponentBuilder.EnchantmentGlintOverride with { Value = true };
        second[DataComponentType.Damage] = ComponentBuilder.Damage with { Value = 5 };

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());

        second[DataComponentType.Damage] = ComponentBuilder.Damage with { Value = 6 };
        Assert.NotEqual(first, second);
    }
}
