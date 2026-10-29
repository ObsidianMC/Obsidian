using Obsidian.API.Inventory;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.API.Registries;
using Xunit;

namespace Obsidian.Tests;

public class BlockUseBypassTests
{
    // Vanilla: sneaking with an item in either hand skips the block's use, but with nothing in hand (or an empty
    // stack) a lever still toggles.
    [Theory]
    [InlineData(true, 1, null, true)]
    [InlineData(true, null, 1, true)]
    [InlineData(true, null, null, false)]
    [InlineData(true, 0, null, false)]
    [InlineData(false, 1, null, false)]
    public void SneakingWithAnItemBypassesBlockUse(bool sneaking, int? mainHand, int? offHand, bool bypasses)
    {
        Assert.Equal(bypasses, UseItemOnPacket.BypassesBlockUse(sneaking, Apples(mainHand), Apples(offHand)));

        static ItemStack Apples(int? count) => count is int value ? new ItemStack(ItemsRegistry.Apple, value) : null;
    }
}
