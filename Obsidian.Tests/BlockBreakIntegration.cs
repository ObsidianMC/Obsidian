using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.API.World;
using System;
using Obsidian.API.Registries;
using Obsidian.Events;
using Obsidian.Entities;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Breaks blocks through the real <see cref="MainEventHandler.OnBlockBreak"/> on a server world and checks the block and the
/// item entities that the break leaves behind.
/// </summary>
public class BlockBreakIntegration
{
    internal static readonly Vector Spot = new(4, 64, 4);

    [Fact]
    public async Task StoneBrokenWithPickaxeDropsCobblestone()
    {
        await using var context = TestServerFactory.CreateServer("breaks-stone");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Pick");
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.DiamondPickaxe, 1));

        var drops = await BreakAsync(context, player, "minecraft:stone");

        Assert.Equal(["minecraft:cobblestone*1"], drops);
        Assert.True((await context.World.GetBlockAsync(Spot))!.IsAir);
    }

    [Fact]
    public async Task StoneBrokenWithBareHandsRemovesBlockWithoutDrops()
    {
        await using var context = TestServerFactory.CreateServer("breaks-bare");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Bare");

        var drops = await BreakAsync(context, player, "minecraft:stone");

        Assert.Empty(drops);
        Assert.True((await context.World.GetBlockAsync(Spot))!.IsAir);
    }

    [Fact]
    public async Task StoneBrokenWithSilkTouchDropsItself()
    {
        await using var context = TestServerFactory.CreateServer("breaks-silk");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Silk");
        var pickaxe = new ItemStack(ItemsRegistry.DiamondPickaxe, 1);
        EnchantmentHelper.Enchant(pickaxe, EnchantmentsRegistry.SilkTouch, 1);
        player.Inventory.SetItem(player.CurrentHeldItemSlot, pickaxe);

        var drops = await BreakAsync(context, player, "minecraft:stone");

        Assert.Equal(["minecraft:stone*1"], drops);
    }

    [Fact]
    public async Task CreativeBreakDropsNothing()
    {
        await using var context = TestServerFactory.CreateServer("breaks-creative");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Creative");
        player.GameMode = GameMode.Creative;
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.DiamondPickaxe, 1));

        var drops = await BreakAsync(context, player, "minecraft:stone");

        Assert.Empty(drops);
        Assert.True((await context.World.GetBlockAsync(Spot))!.IsAir);
    }

    [Fact]
    public async Task DoTileDropsOffDropsNothing()
    {
        await using var context = TestServerFactory.CreateServer("breaks-rule");
        context.World.LevelData.GameRules["do_tile_drops"] = "false";
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Rule");
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.DiamondPickaxe, 1));

        var drops = await BreakAsync(context, player, "minecraft:stone");

        Assert.Empty(drops);
    }

    [Fact]
    public async Task CancelledBreakKeepsBlockAndDropsNothing()
    {
        await using var context = TestServerFactory.CreateServer("breaks-cancel");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Cancel");
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.DiamondPickaxe, 1));

        var drops = await BreakAsync(context, player, "minecraft:stone", cancel: true);

        Assert.Empty(drops);
        Assert.Equal("minecraft:stone", (await context.World.GetBlockAsync(Spot))!.UnlocalizedName);
    }

    [Fact]
    public async Task GrassBlockWithPickaxeDropsDirt()
    {
        await using var context = TestServerFactory.CreateServer("breaks-grass");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Grass");
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.DiamondPickaxe, 1));

        var drops = await BreakAsync(context, player, "minecraft:grass_block");

        Assert.Equal(["minecraft:dirt*1"], drops);
    }

    /// <summary>
    /// Places <paramref name="blockName"/> at <see cref="Spot"/>, breaks it as <paramref name="player"/> and returns the
    /// item stacks of the item entities that spawned, as "item*count".
    /// </summary>
    internal static async Task<string[]> BreakAsync(RealServerContext context, Obsidian.Entities.Player player, string blockName, bool cancel = false)
    {
        var block = BlocksRegistry.Get(blockName);
        await context.World.SetBlockAsync(Spot, block, true);

        var args = CreateArgs(context, player, block, Spot);
        if (cancel)
            args.Cancel();

        await new MainEventHandler().OnBlockBreak(args);

        return context.World.GetEntitiesInRange(new VectorD(Spot.X + 0.5, Spot.Y + 0.5, Spot.Z + 0.5), 4f)
            .OfType<ItemEntity>()
            .Select(entity => $"{entity.Item.Holder.UnlocalizedName}*{entity.Item.Count}")
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray();
    }

    internal static BlockBreakEventArgs CreateArgs(RealServerContext context, Obsidian.Entities.Player player, IBlock block, Vector location)
    {
        // The event args are only created by the server; tests build them the same way, bypassing the visibility.
        var constructor = typeof(BlockBreakEventArgs).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(IServer), typeof(IPlayer), typeof(IBlock), typeof(Vector), typeof(ILevel)])!;

        return (BlockBreakEventArgs)constructor.Invoke([context.Server, player, block, location, context.World]);
    }
}
