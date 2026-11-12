#nullable enable

using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Obsidian.WorldData.Redstone;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// A dispenser spends the item it fires and fires it in one step: an item is never both fired and taken by a player's
/// click or a hopper, nor fired twice. It waits for its inventory only outside the level's tick lock.
/// </summary>
[Collection(nameof(PluginTagFiles))]
public sealed class DispenserAtomicity
{
    private static readonly Vector dispenser = new(8, 65, 8);

    [Fact]
    public async Task FiringWaitsForAClickAndNeverFiresTheItemTheClickTook()
    {
        await using var context = TestServerFactory.CreateServer($"dispenser-click-{Guid.NewGuid():N}");
        var world = context.World;
        var (state, container) = await PlaceDispenserAsync(world, arrows: 1);

        // A click holds the dispenser's inventory while the dispenser fires.
        var click = await container.Inventory.LockAsync();
        var firing = RedstoneDispenser.FireAsync(world, dispenser, state, dropper: false).AsTask();
        Assert.False(firing.IsCompleted);

        // The dispenser waits outside the level's tick lock, which the rest of the level can still take.
        var tickLockFree = await Task.Run(() =>
        {
            if (!world.Fluids.TickLock.TryEnter())
                return false;

            world.Fluids.TickLock.Exit();
            return true;
        });
        Assert.True(tickLockFree);

        // The click takes the arrow, then lets go: the dispenser finds nothing to fire.
        container.Inventory.SetItem(0, null);
        click.Dispose();
        await firing;

        Assert.Equal(0, Arrows(world));
        Assert.Null(container.Inventory.GetItem(0));
    }

    [Fact]
    public async Task BusyCommitKeepsTheBucketAndRetryDeliversItsResultOnce()
    {
        await using var context = TestServerFactory.CreateServer($"dispenser-bucket-busy-{Guid.NewGuid():N}");
        var world = context.World;
        var (state, _) = await PlaceDispenserAsync(world, arrows: 0);
        var inventory = new ObservedInventory();
        var container = new ContainerBlockEntity(inventory);
        await world.SetBlockEntity(dispenser, container);
        inventory.SetItem(0, new ItemStack(ItemsRegistry.Bucket, 2));
        var target = dispenser + Vector.East;
        world.BlockUpdates.SetBlock(target, BlocksRegistry.Water,
            BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);

        // A held tick gate stops preparation after its inventory snapshot. The observed read lets the click
        // queue behind that snapshot, then own inventory before preparation can enter tick.
        var tickHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseTick = new ManualResetEventSlim();
        var holder = Task.Run(() =>
        {
            lock (world.Fluids.TickLock)
            {
                tickHeld.SetResult();
                releaseTick.Wait();
            }
        });
        await tickHeld.Task;
        var snapshotRead = inventory.ObserveNextRead();
        Task? firing = null;
        try
        {
            firing = Task.Run(async () =>
                await RedstoneDispenser.FireAsync(world, dispenser, state, dropper: false));
            await snapshotRead;
            using (await inventory.LockAsync())
            {
                releaseTick.Set();
                await firing;

                Assert.Equal(2, inventory.GetItem(0)!.Count);
                Assert.Null(inventory.GetItem(1));
                Assert.True(world.BlockUpdates.GetBlock(target).Is(BlockIds.Water));
            }
        }
        finally
        {
            releaseTick.Set();
            await holder;
            if (firing is not null)
                await firing;
        }

        // Once the click releases inventory, one retry spends one bucket and returns exactly one filled bucket.
        await RedstoneDispenser.FireAsync(world, dispenser, state, dropper: false);
        Assert.Equal(1, inventory.GetItem(0)!.Count);
        Assert.Equal(ItemIds.WaterBucket, inventory.GetItem(1)!.Holder.Id);
        Assert.Equal(1, inventory.GetItem(1)!.Count);
        Assert.Empty(world.GetEntitiesInRange((VectorD)dispenser, 16).OfType<ItemEntity>());
        Assert.True(world.BlockUpdates.GetBlock(target).IsAir);
    }

    [Fact]
    public async Task BottleExchangeDeliversOneResultToTheSlotInventoryOrOverflow()
    {
        await using var context = TestServerFactory.CreateServer($"dispenser-bottle-results-{Guid.NewGuid():N}");
        var world = context.World;
        var (state, container) = await PlaceDispenserAsync(world, arrows: 0);
        var inventory = container.Inventory;
        var target = dispenser + Vector.East;
        world.BlockUpdates.SetBlock(target, BlocksRegistry.Water,
            BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);

        foreach (var (slots, count) in new[] { (1, 1), (1, 2), (9, 64) })
        {
            using (await inventory.LockAsync())
            {
                for (var slot = 0; slot < inventory.Size; slot++)
                    inventory.SetItem(slot, slot < slots ? new ItemStack(ItemsRegistry.GlassBottle, count) : null);
            }

            var beforeDrops = DroppedPotions(world);
            await RedstoneDispenser.FireAsync(world, dispenser, state, dropper: false);

            using (await inventory.LockAsync())
            {
                var left = inventory.Where(item => item is { Holder.Id: ItemIds.GlassBottle })
                    .Sum(item => item.Count);
                var returned = inventory.Where(item => item is { Holder.Id: ItemIds.Potion })
                    .Sum(item => item.Count);
                Assert.Equal(slots * count - 1, left);
                Assert.Equal(1, returned + DroppedPotions(world) - beforeDrops);
            }
        }
    }

    private static int DroppedPotions(WorldData.World world) => world
        .GetEntitiesInRange((VectorD)dispenser, 16)
        .OfType<ItemEntity>()
        .Where(entity => entity.Item.Holder.Id == ItemIds.Potion)
        .Sum(entity => entity.Item.Count);

    [Fact]
    public async Task FailedBucketWriteKeepsTheFilledItemWithoutSpawningItsContents()
    {
        await using var context = TestServerFactory.CreateServer($"dispenser-unavailable-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        var position = new Vector(15, 65, 8);
        var target = position + Vector.East;
        await world.GetBlockAsync(position);
        var state = BlocksRegistry.Dispenser.WithProperty("facing", "east");
        world.BlockUpdates.SetBlock(position, state,
            BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
        var container = Assert.IsAssignableFrom<ContainerBlockEntity>(world.GetResidentBlockEntity(position));

        // Only the dispenser's chunk is resident. No unload task or chance overlap is needed to force the write
        // failure: preparation reads void air in its unavailable neighbour and SetBlock must refuse that target.
        Assert.Null(world.GetLoadedChunk(1, 0));
        Item[] buckets =
        [
            ItemsRegistry.WaterBucket, ItemsRegistry.LavaBucket,
            ItemsRegistry.PowderSnowBucket, ItemsRegistry.AxolotlBucket
        ];
        foreach (var bucket in buckets)
        {
            using (await container.Inventory.LockAsync())
                container.Inventory.SetItem(0, new ItemStack(bucket, 1));

            await RedstoneDispenser.FireAsync(world, position, state, dropper: false);

            using (await container.Inventory.LockAsync())
            {
                var stored = container.Inventory.Where(item => item is not null).ToArray();
                var retained = Assert.Single(stored);
                Assert.Equal(bucket.Id, retained.Holder.Id);
                Assert.Equal(1, retained.Count);
            }

            Assert.Null(world.GetLoadedChunk(1, 0));
            Assert.True(world.BlockUpdates.GetBlock(target).IsAir);
            Assert.Empty(world.GetEntitiesInRange((VectorD)position, 16).OfType<Mob>());
            Assert.Empty(world.GetEntitiesInRange((VectorD)position, 16).OfType<ItemEntity>());
        }
    }

    private sealed class ObservedInventory() : BaseContainer(9)
    {
        private TaskCompletionSource? nextRead;

        internal Task ObserveNextRead()
        {
            this.nextRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return this.nextRead.Task;
        }

        public override ItemStack? GetItem(int slot)
        {
            Interlocked.Exchange(ref this.nextRead, null)?.SetResult();
            return base.GetItem(slot);
        }
    }

    private static async Task<(IBlock State, ContainerBlockEntity Container)> PlaceDispenserAsync(
        WorldData.World world, int arrows)
    {
        WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(dispenser);

        var state = BlocksRegistry.Dispenser.WithProperty("facing", "east");
        world.BlockUpdates.SetBlock(dispenser, state, BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
        var container = Assert.IsAssignableFrom<ContainerBlockEntity>(world.GetResidentBlockEntity(dispenser));
        container.Inventory.SetItem(0, new ItemStack(ItemsRegistry.Arrow, arrows));
        return (state, container);
    }

    private static int Arrows(WorldData.World world) =>
        world.GetEntitiesInRange((VectorD)dispenser, 16).OfType<Arrow>().Count();
}
