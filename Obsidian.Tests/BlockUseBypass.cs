using Obsidian.API.Inventory;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.API.Registries;
using Xunit;

namespace Obsidian.Tests;

public class BlockUseBypassTests
{
    [Fact]
    public void SneakingWithAnItemInEitherHandBypassesBlockUse()
    {
        var stone = new ItemStack(ItemsRegistry.Apple, 1);
        Assert.True(UseItemOnPacket.BypassesBlockUse(true, stone, null));
        Assert.True(UseItemOnPacket.BypassesBlockUse(true, null, stone));
    }

    [Fact]
    public void SneakingWithEmptyHandsStillUsesTheBlock()
    {
        // Vanilla: a lever toggles when sneaking with nothing in hand.
        Assert.False(UseItemOnPacket.BypassesBlockUse(true, null, null));
        Assert.False(UseItemOnPacket.BypassesBlockUse(true, new ItemStack(ItemsRegistry.Apple, 0), null));
    }

    [Fact]
    public void NotSneakingNeverBypassesBlockUse()
    {
        Assert.False(UseItemOnPacket.BypassesBlockUse(false, new ItemStack(ItemsRegistry.Apple, 1), null));
    }
}
