using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ServerPlayer = Obsidian.Entities.Player;

namespace Obsidian.Tests;

/// <summary>Harvest transactions must not duplicate resources, lose bottles or wait on an inventory under the tick.</summary>
public sealed class FoodHarvestTransactions
{
    private static readonly Vector target = new(8, 64, 8);

    private const BlockUpdateFlags Quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;

    private sealed record Harvest(IBlock Block, Item Item, bool WithItem);

    private static Harvest[] Harvests() =>
    [
        new(BlocksRegistry.Beehive.WithProperty("honey_level", 5), ItemsRegistry.GlassBottle, true),
        new(BlocksRegistry.SweetBerryBush.WithProperty("age", 3), ItemsRegistry.Shears, false)
    ];

    [Fact]
    public async Task AHandChangedAfterReadingCannotHarvestOrExchange()
    {
        await using var context = await StartAsync("harvest-stale");
        var player = Player(context, "User");

        foreach (var harvest in Harvests())
        {
            foreach (var change in new[] { "replace", "count", "slot" })
            {
                player.CurrentHeldItemSlot = 0;
                var original = new ItemStack(harvest.Item, 2);
                player.Inventory.SetItem(36, original);
                context.World.BlockUpdates.SetBlock(target, harvest.Block, Quiet);
                var use = BlockUseTransactions.Read(context, player);

                if (change == "replace")
                    player.Inventory.SetItem(36, new ItemStack(ItemsRegistry.Diamond, 2));
                else if (change == "count")
                    original.Count = 1;
                else
                    player.CurrentHeldItemSlot = 1;

                var result = await UseAsync(use, harvest);
                await context.World.RunQueuedEntityActionsAsync();

                Assert.Equal(UseResult.Fail, result);
                Assert.True(context.World.BlockUpdates.GetBlock(target).IsSameState(harvest.Block));
                Assert.Equal(change == "count" ? 1 : 2, original.Count);
                Assert.Empty(context.World.GetEntitiesInRange((VectorD)target, 5).OfType<ItemEntity>());
                Assert.DoesNotContain(player.Inventory, item => item?.Holder.Id == ItemIds.HoneyBottle);
            }
        }
    }

    [Fact]
    public async Task AFullInventoryDropsOnlyTheExchangedBottle()
    {
        await using var context = await StartAsync("harvest-full");
        var player = Player(context, "User");
        var harvest = Harvests()[0];
        foreach (var slot in Enumerable.Range(9, 36))
            player.Inventory.SetItem(slot, new ItemStack(ItemsRegistry.Stone, 64));

        player.Inventory.SetItem(36, new ItemStack(ItemsRegistry.GlassBottle, 2));
        context.World.BlockUpdates.SetBlock(target, harvest.Block, Quiet);
        var use = BlockUseTransactions.Read(context, player);

        Assert.Equal(UseResult.Success, await UseAsync(use, harvest));
        Assert.Equal(UseResult.Fail, await UseAsync(use, harvest));
        await context.World.RunQueuedEntityActionsAsync();

        Assert.Equal(1, player.GetHeldItem()!.Count);
        Assert.Equal(ItemIds.GlassBottle, player.GetHeldItem()!.Holder.Id);
        var dropped = Assert.Single(context.World.GetEntitiesInRange((VectorD)target, 5).OfType<ItemEntity>());
        Assert.Equal(ItemIds.HoneyBottle, dropped.Item.Holder.Id);
        Assert.Equal(1, dropped.Item.Count);
        Assert.Equal(35 * 64, player.Inventory.Where(item => item?.Holder.Id == ItemIds.Stone).Sum(item => item!.Count));
    }

    [Fact]
    public async Task TwoPlayersAtOneHiveOrBushReceiveOneHarvest()
    {
        await using var context = await StartAsync("harvest-race");
        var first = Player(context, "First");
        var second = Player(context, "Second");

        foreach (var harvest in Harvests())
        {
            context.World.BlockUpdates.SetBlock(target, harvest.Block, Quiet);
            BlockLoot.SequencesFor(context.World).Get("minecraft:harvest/sweet_berry_bush").SetSeed(1234);
            foreach (var player in new[] { first, second })
                player.Inventory.SetItem(36, new ItemStack(harvest.Item, 1));

            var uses = new[] { first, second }.Select(player => BlockUseTransactions.Read(context, player)).ToArray();
            var results = await Task.WhenAll(uses.Select(use => Task.Run(() => UseAsync(use, harvest))));
            await context.World.RunQueuedEntityActionsAsync();

            Assert.Single(results, result => result == UseResult.Success);
            Assert.Single(results, result => result == UseResult.Fail);
            if (harvest.WithItem)
            {
                var hands = new[] { first.GetHeldItem()!, second.GetHeldItem()! };
                Assert.Equal(1, hands.Where(item => item.Holder.Id == ItemIds.HoneyBottle).Sum(item => item.Count));
                Assert.Equal(1, hands.Where(item => item.Holder.Id == ItemIds.GlassBottle).Sum(item => item.Count));
            }
            else
            {
                // This seeded harvest's two stacks are recorded in berries_age_3_hand. Racing cannot double them.
                var drops = context.World.GetEntitiesInRange((VectorD)target, 5).OfType<ItemEntity>();
                Assert.Equal(2, drops.Where(item => item.Item.Holder.Id == ItemIds.SweetBerries)
                    .Sum(item => item.Item.Count));
            }
        }
    }

    [Fact]
    public async Task ABusyInventoryIsTriedWhileTheTickLockIsHeld()
    {
        await using var context = await StartAsync("harvest-busy");
        var player = Player(context, "User");

        foreach (var harvest in Harvests())
        {
            player.Inventory.SetItem(36, new ItemStack(harvest.Item, 2));
            context.World.BlockUpdates.SetBlock(target, harvest.Block, Quiet);
            var use = BlockUseTransactions.Read(context, player);
            UseResult result;
            using (player.Inventory.Lock())
            {
                result = await Task.Run(() =>
                {
                    lock (context.World.Fluids.TickLock)
                        return UseAsync(use, harvest).GetAwaiter().GetResult();
                }).WaitAsync(TimeSpan.FromSeconds(10));
            }

            Assert.Equal(UseResult.Fail, result);
            Assert.True(context.World.BlockUpdates.GetBlock(target).IsSameState(harvest.Block));
            Assert.Equal(2, player.GetHeldItem()!.Count);
            Assert.Empty(context.World.GetEntitiesInRange((VectorD)target, 5).OfType<ItemEntity>());
        }
    }

    private static async Task<UseResult> UseAsync(BlockUse use, Harvest harvest)
    {
        var behavior = harvest.WithItem ? BlocksRegistry.GetUseItemOnBehavior(use.Block.RegistryId)
            : BlocksRegistry.GetUseWithoutItemBehavior(use.Block.RegistryId);
        Assert.NotNull(behavior);
        return harvest.WithItem ? await behavior.UseItemOnAsync(use) : await behavior.UseWithoutItemAsync(use);
    }

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
            new VectorD(8.5, 64, 6.5));
        context.World.TryAddPlayer(player);
        player.GameMode = GameMode.Survival;
        return player;
    }
}
