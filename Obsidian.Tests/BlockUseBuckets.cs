using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Events;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Obsidian.Registries;
using ServerPlayer = Obsidian.Entities.Player;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class BlockUseBuckets
{
    private static readonly Vector target = new(8, 64, 8);
    private const BlockUpdateFlags Quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;

    [Fact]
    public async Task StackedBucketsRouteExactlyOneResultIncludingWhenStorageIsFull()
    {
        await using var context = TestServerFactory.CreateServer($"bucket-accounting-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        await context.World.GetBlockAsync(target);
        var player = Player(context);

        foreach (var full in new[] { false, true })
        {
            for (var slot = 9; slot <= 45; slot++)
                player.Inventory.SetItem(slot, full ? new ItemStack(ItemsRegistry.Stone, 64) : null);

            var input = new ItemStack(ItemsRegistry.Bucket, 3);
            player.Inventory.SetItem(player.CurrentHeldItemSlot, input);
            context.World.BlockUpdates.SetBlock(target, BlocksRegistry.Water, Quiet);
            var use = BlockUseTransactions.Read(context, player);

            Assert.Equal(UseResult.Success, await BucketItems.ApplyAsync(use));
            Assert.Equal(2, input.Count);
            var stored = Enumerable.Range(9, 37)
                .Select(player.Inventory.GetItem)
                .Where(item => item?.Holder.Id == ItemsRegistry.WaterBucket.Id)
                .Sum(item => item.Count);
            var dropped = context.World.GetEntitiesInRange(player.Position, 8)
                .OfType<ItemEntity>()
                .Where(entity => entity.Item.Holder.Id == ItemsRegistry.WaterBucket.Id)
                .Sum(entity => entity.Item.Count);
            Assert.Equal(full ? 0 : 1, stored);
            Assert.Equal(full ? 1 : 0, dropped);
            Assert.Equal(1, stored + dropped);

            // Reusing a consumed snapshot must not duplicate another result or consume a second bucket.
            Assert.Equal(UseResult.Fail, await BucketItems.ApplyAsync(use));
            Assert.Equal(2, input.Count);
        }
    }

    [Fact]
    public async Task ChangedHandAndBusyInventoryRefuseWithoutConsumingOrTakingTheSource()
    {
        await using var context = TestServerFactory.CreateServer($"bucket-stale-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        await context.World.GetBlockAsync(target);
        var player = Player(context);

        foreach (var change in new[] { "replace", "count", "slot", "busy" })
        {
            player.CurrentHeldItemSlot = 0;
            var input = new ItemStack(ItemsRegistry.Bucket, 3);
            player.Inventory.SetItem(36, input);
            context.World.BlockUpdates.SetBlock(target, BlocksRegistry.Water, Quiet);
            var use = BlockUseTransactions.Read(context, player);
            IDisposable held = null;
            switch (change)
            {
                case "replace":
                    player.Inventory.SetItem(36, new ItemStack(ItemsRegistry.Diamond, 2));
                    break;
                case "count":
                    input.Count = 2;
                    break;
                case "slot":
                    player.CurrentHeldItemSlot = 1;
                    break;
                case "busy":
                    held = player.Inventory.Lock();
                    break;
            }

            try
            {
                var result = await Task.Run(async () =>
                {
                    ValueTask<UseResult> action;
                    lock (context.World.Fluids.TickLock)
                        action = BucketItems.ApplyAsync(use);

                    return await action;
                }).WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(UseResult.Fail, result);
            }
            finally
            {
                held?.Dispose();
            }

            Assert.True(context.World.BlockUpdates.GetBlock(target).Is(BlockIds.Water), change);
            Assert.Equal(change == "count" ? 2 : 3, input.Count);
            Assert.DoesNotContain(Enumerable.Range(9, 37).Select(player.Inventory.GetItem),
                item => item?.Holder.Id == ItemsRegistry.WaterBucket.Id);
            Assert.Empty(context.World.GetEntitiesInRange(player.Position, 8).OfType<ItemEntity>());
        }
    }

    [Fact]
    public async Task AHandChangedDuringThePlacementEventCannotPourForFree()
    {
        await using var context = TestServerFactory.CreateServer($"bucket-plugin-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        await context.World.GetBlockAsync(target);
        var player = Player(context);
        var input = new ItemStack(ItemsRegistry.WaterBucket, 1);
        player.Inventory.SetItem(36, input);
        context.World.BlockUpdates.SetBlock(target, BlocksRegistry.Stone, Quiet);
        var use = BlockUseTransactions.Read(context, player);
        var replacement = new ItemStack(ItemsRegistry.Diamond, 2);
        var eventRan = false;
        context.EventDispatcher.RegisterEvent(null, (Func<BlockPlaceEventArgs, ValueTask>)(async _ =>
        {
            await Task.Yield();
            using (await player.Inventory.LockAsync())
                player.Inventory.SetItem(36, replacement);

            eventRan = true;
        }));

        var result = await BucketItems.ApplyAsync(use);

        Assert.True(eventRan);
        Assert.Equal(UseResult.Fail, result);
        Assert.Same(replacement, player.GetHeldItem());
        Assert.Equal(2, replacement.Count);
        Assert.Equal(1, input.Count);
        Assert.True(context.World.BlockUpdates.GetBlock(target + Vector.Up).IsAir);
    }

    [Fact]
    public async Task AnUnconfirmedTeleportRefusesTheItemPacketWithoutApplyingItsAim()
    {
        await using var context = TestServerFactory.CreateServer($"bucket-teleport-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        await context.World.GetBlockAsync(target);
        var player = Player(context);
        player.Inventory.SetItem(36, new ItemStack(ItemsRegistry.WaterBucket, 1));
        player.Yaw = 33;
        player.Pitch = 7;
        var yaw = player.Yaw.Degrees;
        var pitch = player.Pitch.Degrees;
        player.TeleportId = 42;
        context.World.BlockUpdates.SetBlock(target, BlocksRegistry.Stone, Quiet);
        var packet = new UseItemPacket();
        typeof(UseItemPacket).GetProperty(nameof(UseItemPacket.Yaw))!.SetValue(packet, 0f);
        typeof(UseItemPacket).GetProperty(nameof(UseItemPacket.Pitch))!.SetValue(packet, 90f);

        Assert.Equal(UseResult.Fail, await packet.UseAsync(context.Server, player));

        Assert.Equal(yaw, player.Yaw.Degrees);
        Assert.Equal(pitch, player.Pitch.Degrees);
        Assert.Equal(ItemsRegistry.WaterBucket.Id, player.GetHeldItem()?.Holder.Id);
        Assert.True(context.World.BlockUpdates.GetBlock(target + Vector.Up).IsAir);
    }

    [Fact]
    public async Task ConcurrentPickupsCannotFillTwoBucketsFromOneSource()
    {
        await using var context = TestServerFactory.CreateServer($"bucket-race-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        await context.World.GetBlockAsync(target);
        var first = Player(context);
        var second = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Other", first.Position);
        context.World.TryAddPlayer(second);
        second.GameMode = GameMode.Survival;
        first.Inventory.SetItem(36, new ItemStack(ItemsRegistry.Bucket, 1));
        second.Inventory.SetItem(36, new ItemStack(ItemsRegistry.Bucket, 1));
        context.World.BlockUpdates.SetBlock(target, BlocksRegistry.Water, Quiet);
        var uses = new[] { BlockUseTransactions.Read(context, first), BlockUseTransactions.Read(context, second) };

        var results = await Task.WhenAll(uses.Select(use => Task.Run(async () => await BucketItems.ApplyAsync(use))))
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Single(results.Where(result => result == UseResult.Success));
        Assert.Single(new[] { first, second }.Where(player => player.GetHeldItem()?.Holder.Id == ItemIds.WaterBucket));
        Assert.Single(new[] { first, second }.Where(player => player.GetHeldItem()?.Holder.Id == ItemIds.Bucket));
    }

    private static ServerPlayer Player(RealServerContext context)
    {
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "User",
            new VectorD(8.5, 66, 8.5));
        context.World.TryAddPlayer(player);
        player.GameMode = GameMode.Survival;
        return player;
    }
}
