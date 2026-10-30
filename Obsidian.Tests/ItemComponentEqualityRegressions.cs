using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using System.Collections.Generic;
using Xunit;
using PlayerEntity = Obsidian.Entities.Player;

namespace Obsidian.Tests;

public sealed partial class ItemComponentEqualityRegressions
{
    [Theory]
    [InlineData(DataComponentType.WrittenBookContent)]
    [InlineData(DataComponentType.AttributeModifiers)]
    public void ChatKeybindDifferencesPreventMerging(DataComponentType type)
    {
        var first = ChatComponent(type, new ChatMessage { Keybind = "key.jump" });
        var second = ChatComponent(type, new ChatMessage { Keybind = "key.sneak" });

        AssertDoNotMerge(first, second);
    }

    private static DataComponent ChatComponent(DataComponentType type, ChatMessage chat) => type switch
    {
        DataComponentType.WrittenBookContent => new WrittenBookDataComponent
        {
            Title = "Book",
            Author = "Author",
            Pages = [(chat, null)]
        },
        DataComponentType.AttributeModifiers => new AttributeModifiersDataComponent
        {
            Value = [],
            Displays = [(2, chat)]
        },
        _ => throw new System.ArgumentOutOfRangeException(nameof(type))
    };

    private static void AssertDoNotMerge(DataComponent first, DataComponent second)
    {
        var stored = new ItemStack(ItemsRegistry.Diamond, 2, first);
        var incoming = new ItemStack(ItemsRegistry.Diamond, 2, second);
        var inventory = new Container(46);
        inventory.SetItem(36, stored);

        var changed = PlayerEntity.AddToInventory(inventory, 36, incoming);

        Assert.NotEqual(first, second);
        Assert.False(first.Equals((object)second));
        Assert.NotEqual(stored, incoming);
        Assert.Equal([37], changed);
        Assert.Equal(2, inventory.GetItem(36)!.Count);
        Assert.Equal(2, inventory.GetItem(37)!.Count);
    }

    private static void AssertMerge(DataComponent first, DataComponent second)
    {
        var stored = new ItemStack(ItemsRegistry.Diamond, 2, first);
        var incoming = new ItemStack(ItemsRegistry.Diamond, 2, second);
        var inventory = new Container(46);
        inventory.SetItem(36, stored);
        var lookup = new Dictionary<DataComponent, string> { [first] = "found" };

        var changed = PlayerEntity.AddToInventory(inventory, 36, incoming);

        Assert.Equal(first, second);
        Assert.True(first.Equals((object)second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal("found", lookup[second]);
        Assert.Equal(stored, incoming);
        Assert.Equal(stored.GetHashCode(), incoming.GetHashCode());
        Assert.Equal([36], changed);
        Assert.Equal(4, inventory.GetItem(36)!.Count);
        Assert.Null(inventory.GetItem(37));
    }
}
