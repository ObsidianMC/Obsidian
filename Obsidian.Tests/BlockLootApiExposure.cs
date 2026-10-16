using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// The block loot a plugin can reach through the public API: the generated tables in <see cref="LootTables"/> and
/// <see cref="Obsidian.API.World.ILevel.GetBlockDropsAsync"/>.
/// </summary>
public class BlockLootApiExposure
{
    private static readonly Obsidian.API.Vector Spot = BlockBreakIntegration.Spot;

    [Fact]
    public void AllVanillaBlockTablesAreInTheRegistry()
    {
        // 1.21.11 has 1,083 block loot tables (one per block state family, from the vanilla data).
        var blockTables = LootTables.All.Keys.Where(id => id.StartsWith("minecraft:blocks/", System.StringComparison.Ordinal)).ToList();

        Assert.Equal(1083, blockTables.Count);
        Assert.Same(LootTables.Blocks.Stone, LootTables.All["minecraft:blocks/stone"]);
        Assert.Equal("minecraft:blocks/stone", LootTables.Blocks.Stone.Identifier);
    }

    [Fact]
    public async Task DirtDropsItselfWithoutATool()
    {
        await using var context = TestServerFactory.CreateServer("api-drops-dirt");
        await context.World.SetBlockAsync(Spot, BlocksRegistry.Get("minecraft:dirt"), true);

        var drops = await context.World.GetBlockDropsAsync(Spot);

        Assert.Equal("minecraft:dirt", Assert.Single(drops).Holder.UnlocalizedName);
    }

    [Fact]
    public async Task StoneDropsNothingWithBareHandsAndCobblestoneWithAPickaxe()
    {
        await using var context = TestServerFactory.CreateServer("api-drops-stone");
        await context.World.SetBlockAsync(Spot, BlocksRegistry.Get("minecraft:stone"), true);

        Assert.Empty(await context.World.GetBlockDropsAsync(Spot));

        var pickaxe = new ItemStack(ItemsRegistry.DiamondPickaxe, 1);
        var drops = await context.World.GetBlockDropsAsync(Spot, pickaxe);
        Assert.Equal("minecraft:cobblestone", Assert.Single(drops).Holder.UnlocalizedName);
    }

    [Fact]
    public async Task AirAndDoTileDropsOffGiveNothing()
    {
        await using var context = TestServerFactory.CreateServer("api-drops-none");
        await context.World.SetBlockAsync(Spot, BlocksRegistry.Air, true);
        Assert.Empty(await context.World.GetBlockDropsAsync(Spot));

        await context.World.SetBlockAsync(Spot, BlocksRegistry.Get("minecraft:dirt"), true);
        context.World.LevelData.GameRules["do_tile_drops"] = "false";
        Assert.Empty(await context.World.GetBlockDropsAsync(Spot));
    }
}
