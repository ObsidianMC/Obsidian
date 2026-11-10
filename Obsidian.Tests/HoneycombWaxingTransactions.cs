using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ServerPlayer = Obsidian.Entities.Player;

namespace Obsidian.Tests;

/// <summary>
/// Waxing changes the block and the hand together: honeycomb is never spent without a wax, and a block is never
/// waxed twice for one honeycomb, whatever happens to the hand or the block during the use.
/// </summary>
public sealed class HoneycombWaxingTransactions
{
    // BlockUseTransactions.Read uses the block here.
    private static readonly Vector target = new(8, 64, 8);

    private const BlockUpdateFlags Quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;

    [Fact]
    public async Task AChangedOrBusyHandRefusesWithoutSpendingOrWaxing()
    {
        await using var context = await StartAsync("honeycomb-stale");
        var player = Player(context, "User");

        foreach (var change in new[] { "replace", "count", "slot", "busy" })
        {
            player.CurrentHeldItemSlot = 0;
            var honeycomb = new ItemStack(ItemsRegistry.Honeycomb, 3);
            player.Inventory.SetItem(36, honeycomb);
            context.World.BlockUpdates.SetBlock(target, BlocksRegistry.CopperBlock, Quiet);
            var use = BlockUseTransactions.Read(context, player);

            // The hand changes after the click was read, or another holder has the inventory when the use commits.
            IDisposable? held = ChangeHand(player, honeycomb, change);
            try
            {
                Assert.Equal(UseResult.Fail, await WaxUnderTickLockAsync(context, use));
            }
            finally
            {
                held?.Dispose();
            }

            Assert.True(context.World.BlockUpdates.GetBlock(target).Is(BlockIds.CopperBlock), change);
            Assert.Equal(change == "count" ? 2 : 3, honeycomb.Count);
        }
    }

    [Fact]
    public async Task UsesRacingOnOneBlockWaxItOnceForOneHoneycomb()
    {
        await using var context = await StartAsync("honeycomb-race");
        var first = Player(context, "First");
        var second = Player(context, "Second");

        for (var round = 0; round < 20; round++)
        {
            context.World.BlockUpdates.SetBlock(target, BlocksRegistry.ExposedCopper, Quiet);
            foreach (var player in new[] { first, second })
                player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.Honeycomb, 2));

            var uses = new[] { first, second }.Select(player => BlockUseTransactions.Read(context, player)).ToArray();

            var results = await Task.WhenAll(uses.Select(use => Task.Run(() => WaxAsync(use))));

            Assert.Single(results, result => result == UseResult.Success);
            Assert.Single(results, result => result == UseResult.Fail);
            Assert.True(context.World.BlockUpdates.GetBlock(target).Is(BlockIds.WaxedExposedCopper));
            Assert.Equal(3, first.GetHeldItem()!.Count + second.GetHeldItem()!.Count);
        }
    }

    [Fact]
    public async Task AUseThatAlreadyWaxedCannotSpendAgain()
    {
        await using var context = await StartAsync("honeycomb-replay");
        var player = Player(context, "User");
        var honeycomb = new ItemStack(ItemsRegistry.Honeycomb, 3);
        player.Inventory.SetItem(player.CurrentHeldItemSlot, honeycomb);
        context.World.BlockUpdates.SetBlock(target, BlocksRegistry.CopperBlock, Quiet);
        var use = BlockUseTransactions.Read(context, player);

        Assert.Equal(UseResult.Success, await WaxAsync(use));
        Assert.Equal(UseResult.Fail, await WaxAsync(use));

        Assert.True(context.World.BlockUpdates.GetBlock(target).Is(BlockIds.WaxedCopperBlock));
        Assert.Equal(2, honeycomb.Count);
    }

    private static IDisposable? ChangeHand(ServerPlayer player, ItemStack honeycomb, string change)
    {
        switch (change)
        {
            case "replace":
                player.Inventory.SetItem(36, new ItemStack(ItemsRegistry.Diamond, 3));
                return null;
            case "count":
                honeycomb.Count = 2;
                return null;
            case "slot":
                player.CurrentHeldItemSlot = 1;
                return null;
            default:
                return player.Inventory.Lock();
        }
    }

    // A use committed from the level's tick, where a busy inventory must be refused rather than waited for.
    private static Task<UseResult> WaxUnderTickLockAsync(RealServerContext context, BlockUse use) =>
        Task.Run(async () =>
        {
            ValueTask<UseResult> waxing;
            lock (context.World.Fluids.TickLock)
                waxing = CopperWaxing.UseHoneycombAsync(use);

            return await waxing;
        }).WaitAsync(TimeSpan.FromSeconds(10));

    private static async Task<UseResult> WaxAsync(BlockUse use) => await CopperWaxing.UseHoneycombAsync(use);

    private static async Task<RealServerContext> StartAsync(string name)
    {
        var context = TestServerFactory.CreateServer($"{name}-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        await context.World.GetBlockAsync(target);
        return context;
    }

    private static ServerPlayer Player(RealServerContext context, string name)
    {
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, name,
            new VectorD(8.5, 66, 8.5));
        context.World.TryAddPlayer(player);
        player.GameMode = GameMode.Survival;
        return player;
    }
}
