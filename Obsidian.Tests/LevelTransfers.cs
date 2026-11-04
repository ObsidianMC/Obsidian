using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Each level guards its items with its own transfer lock, and travel hands entities from one level's lock to the
/// other's, so that levels can tick on threads of their own. These check that no item is duplicated or lost doing so.
/// </summary>
public sealed class LevelTransfers
{
    private const int ItemCount = 120;
    private const int Rounds = 25;

    // Items this far east are in reach of the level's hopper.
    private const double HopperX = 10;

    private static readonly VectorD Centre = new(8, 70, 8);

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ItemsMovedWithinAndBetweenLevelsTickingInParallelAreNeitherDuplicatedNorLost()
    {
        await using var firstServer = TestServerFactory.CreateServer($"transfers-first-{Guid.NewGuid():N}");
        await using var secondServer = TestServerFactory.CreateServer($"transfers-second-{Guid.NewGuid():N}");
        var first = await PrepareAsync(firstServer, "First");
        var second = await PrepareAsync(secondServer, "Second");
        DropItems(first);

        // Each level ticks on a thread of its own, as the levels of a server will. Between ticks its player picks up,
        // its hopper pulls, and half its items go through to the other level, which takes them in while it ticks.
        await Task.WhenAll(
            Task.Run(() => RunLevelAsync(first, second)),
            Task.Run(() => RunLevelAsync(second, first)))
            .WaitAsync(TimeSpan.FromSeconds(20));

        AssertEveryDiamondOnce(first, second);

        // Holding two levels' transfer locks at once could deadlock levels ticking in parallel, so it's refused.
        using (first.World.TransferLock.Enter())
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                using var both = second.World.TransferLock.Enter();
            });
        }
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task HoppersOfTwoLevelsPullingTheSameItemsAtOnceTakeEachOnce()
    {
        const int count = 300;
        await using var server = TestServerFactory.CreateServer($"transfers-contended-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(server.World);
        var items = Enumerable.Range(0, count)
            .Select(index => Drop(server.World, index, Spot(index)))
            .ToList();
        var own = new ItemAccessOver(new Container(27));
        var other = new ItemAccessOver(new Container(27));

        // The level's hopper and another level's, which found the items in a lookup that raced their travel, pull
        // each item at the same moment. Only the item's own level's lock can keep them from both taking it.
        using var gate = new Barrier(2);
        await Task.WhenAll(
            OnOwnThread(() => PullEach(items, own, gate)),
            OnOwnThread(() => PullEach(items, other, gate)))
            .WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(count, Diamonds(own.Items) + Diamonds(other.Items));
        Assert.Empty(ItemsIn(server.World));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AnItemRemovedWhileTravellingNeverArrives()
    {
        await using var sourceServer = TestServerFactory.CreateServer($"transit-source-{Guid.NewGuid():N}");
        await using var targetServer = TestServerFactory.CreateServer($"transit-target-{Guid.NewGuid():N}");
        var source = sourceServer.World;
        var target = targetServer.World;
        WorldBlockEntities.AddEmptyChunk(source);
        WorldBlockEntities.AddEmptyChunk(target);
        var item = Drop(source, 1, Centre);

        // The target is busy (its lock held) when the item gets there, and removes it before letting it in.
        using var arrived = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var removal = OnOwnThread(() =>
        {
            using (target.TransferLock.Enter())
            {
                arrived.Set();
                Assert.True(SpinWait.SpinUntil(() => item.Level == target, TimeSpan.FromSeconds(10)));
                Assert.True(item.RemoveAsync().IsCompleted);
                release.Wait(TimeSpan.FromSeconds(10));
            }
        });
        arrived.Wait(TimeSpan.FromSeconds(10));
        var travel = Task.Run(() => item.TeleportAsync(target, Centre).AsTask());
        Assert.True(SpinWait.SpinUntil(() => item.Level == target, TimeSpan.FromSeconds(10)));
        release.Set();
        await removal.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.ThrowsAsync<InvalidOperationException>(() => travel.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Empty(ItemsIn(source));
        Assert.Empty(ItemsIn(target));
    }

    private sealed record Side(World World, Entities.Player Player, ItemAccessOver Hopper);

    private static async Task<Side> PrepareAsync(RealServerContext context, string name)
    {
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        for (var x = 0; x < 16; x++)
        {
            for (var z = 0; z < 16; z++)
                await world.SetBlockAsync(new Vector(x, 69, z), BlocksRegistry.Stone);
        }

        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, name, new VectorD(3.5, 70, 3.5));
        Assert.True(world.TryAddPlayer(player));
        Assert.True(world.TryAddEntity(player));

        return new Side(world, player, new ItemAccessOver(new Container(27)));
    }

    private static void DropItems(Side side)
    {
        for (var index = 0; index < ItemCount; index++)
            Drop(side.World, index, Spot(index));
    }

    private static ItemEntity Drop(World world, int index, VectorD position)
    {
        var item = new ItemEntity
        {
            Level = world,
            EntityId = 700_000 + index,
            Position = position,
            Item = new ItemStack(ItemsRegistry.Diamond),
            CanPickup = true
        };
        Assert.True(world.TryAddEntity(item));
        return item;
    }

    // Spread over the floor, so some land by the player, some by the hopper and the rest nowhere in particular.
    private static VectorD Spot(int index) => new(1.5 + index % 13, 70, 1.5 + index / 13 % 13);

    private static async Task RunLevelAsync(Side side, Side other)
    {
        for (var round = 0; round < Rounds; round++)
        {
            // Found before the tick, like a lookup racing the other level: by the time they're pulled below, these
            // may have gone through, been picked up or been pulled there.
            var stale = ItemsIn(other.World);

            await side.World.DoWorldTickAsync();
            await side.Player.PickupNearbyItemsAsync();

            foreach (var item in ItemsIn(side.World))
            {
                if (item.Position.X >= HopperX)
                    await Hoppers.TakeInAsync(side.Hopper, item);
                else if ((item.EntityId + round) % 2 == 0)
                    await SendThroughAsync(item, other.World, Spot(item.EntityId + round));
            }

            foreach (var item in stale.Where(item => item.Position.X >= HopperX))
                await Hoppers.TakeInAsync(side.Hopper, item);
        }
    }

    private static void PullEach(List<ItemEntity> items, IItemAccess hopper, Barrier gate)
    {
        foreach (var item in items)
        {
            Assert.True(gate.SignalAndWait(TimeSpan.FromSeconds(10)));
            Hoppers.TakeInAsync(hopper, item).AsTask().GetAwaiter().GetResult();
        }
    }

    // What a portal does once the destination is ready. The item may have been taken meanwhile; it then stays.
    private static async Task SendThroughAsync(ItemEntity item, World destination, VectorD position)
    {
        try
        {
            await item.TeleportAsync(destination, position);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static List<ItemEntity> ItemsIn(World world) =>
        world.GetNonPlayerEntitiesInRange(Centre, 32).OfType<ItemEntity>().ToList();

    private static void AssertEveryDiamondOnce(params Side[] sides)
    {
        var entities = new HashSet<ItemEntity>(ReferenceEqualityComparer.Instance);
        var diamonds = 0;
        foreach (var side in sides)
        {
            var resident = side.World.Regions.Values
                .SelectMany(region => region.Entities.Values)
                .OfType<ItemEntity>();
            foreach (var item in resident)
            {
                Assert.True(entities.Add(item), $"Item {item.EntityId} is in two places.");
                Assert.Same(side.World, item.Level);
                Assert.False(item.Removed);
                diamonds += item.Item.Count;
            }

            diamonds += Diamonds(side.Player.Inventory);
            diamonds += Diamonds(side.Hopper.Items);
        }

        Assert.Equal(ItemCount, diamonds);
    }

    private static int Diamonds(IEnumerable<ItemStack> stacks) =>
        stacks.Where(stack => stack is not null && stack.Is(ItemsRegistry.Diamond)).Sum(stack => stack.Count);

    private static Task OnOwnThread(Action work) =>
        Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    // A hopper's slots, reached as a hopper reaches the container it fills.
    private sealed class ItemAccessOver(Container items) : IItemAccess
    {
        public Container Items => items;

        public int SlotCount => items.Size;

        public ItemStack? GetItem(int slot) => items.GetItem(slot);

        public ItemStack? Insert(int slot, ItemStack stack, bool simulate = false)
        {
            var held = items.GetItem(slot);
            if (held is not null && held != stack)
                return stack;

            var room = stack.MaxStackSize - (held?.Count ?? 0);
            var moved = Math.Min(room, stack.Count);
            if (moved <= 0)
                return stack;

            if (!simulate)
                items.SetItem(slot, new ItemStack(stack, (held?.Count ?? 0) + moved));

            var left = stack.Count - moved;
            return left > 0 ? new ItemStack(stack, left) : null;
        }

        public ItemStack? Extract(int slot, int count, bool simulate = false) => null;
    }
}
