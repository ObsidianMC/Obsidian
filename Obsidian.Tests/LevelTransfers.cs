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
/// Each level guards its items with its own transfer lock, and travel hands entities from one level to another, so that
/// levels can tick on threads of their own. These check that no item or entity is duplicated or lost doing so.
/// </summary>
public sealed class LevelTransfers
{
    private const int ItemCount = 120;
    private const int OverlappingHandoffs = 10;
    private const int MaxRounds = 400;

    // Items this far east are in reach of the level's hopper.
    private const double HopperX = 10;

    private static readonly VectorD Centre = new(8, 70, 8);
    private static readonly VectorD HopperMin = new(HopperX, 60, 0);
    private static readonly VectorD HopperMax = new(16, 80, 16);

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ItemsSentThroughDuringParallelTicksArriveWithTheDestinationsTick()
    {
        await using var firstServer = TestServerFactory.CreateServer($"transfers-first-{Guid.NewGuid():N}");
        await using var secondServer = TestServerFactory.CreateServer($"transfers-second-{Guid.NewGuid():N}");
        var first = await PrepareAsync(firstServer, "First");
        var second = await PrepareAsync(secondServer, "Second");
        for (var index = 0; index < ItemCount; index++)
            Drop(first.World, index, Spot(index));

        // Each level ticks on a thread of its own, as a server's levels will. During its tick, half its items go
        // through to the other level, as through a portal; after it, its player picks up and its hopper pulls.
        var traffic = new Traffic();
        await Task.WhenAll(
            Task.Run(() => RunLevelAsync(first, second, traffic)),
            Task.Run(() => RunLevelAsync(second, first, traffic)))
            .WaitAsync(TimeSpan.FromSeconds(25));

        // Every handoff succeeded (nothing else touches a level's items in its tick), and enough of them left while
        // the destination was ticking. A flush takes in those still waiting, so the save has them.
        Assert.Equal(0, traffic.Failed);
        Assert.True(traffic.Overlapping >= OverlappingHandoffs, $"Only {traffic.Overlapping} overlapping handoffs.");
        await first.World.FlushRegionsAsync();
        await second.World.FlushRegionsAsync();
        Assert.Equal(0, first.World.PendingArrivalCount);
        Assert.Equal(0, second.World.PendingArrivalCount);
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
    public async Task HoppersAndMobsTakingTheSameItemsAtOnceTakeEachOnce()
    {
        const int count = 300;
        await using var server = TestServerFactory.CreateServer($"transfers-contended-{Guid.NewGuid():N}");
        var world = server.World;
        WorldBlockEntities.AddEmptyChunk(world);
        var items = Enumerable.Range(0, count)
            .Select(index => Drop(world, index, new VectorD(12.5, 70, 1.5 + index % 13)))
            .ToList();
        var hopper = new ItemAccessOver(new Container(27));
        var mob = new List<ItemStack>();

        // A hopper and a mob pull each item at the same moment: only the lock of the item's level keeps them from both
        // taking it.
        using var gate = new Barrier(2);
        await Task.WhenAll(
            OnOwnThread(() => ForEachTogether(items, gate, item => Pull(hopper, item, world))),
            OnOwnThread(() => ForEachTogether(items, gate, item => Equip(mob, item, world))))
            .WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(count, Diamonds(hopper.Items) + Diamonds(mob));
        Assert.Empty(ItemsIn(world));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AnItemThatLeftItsLevelCannotBeTakenThroughAnEarlierLookup()
    {
        await using var sourceServer = TestServerFactory.CreateServer($"stale-source-{Guid.NewGuid():N}");
        await using var targetServer = TestServerFactory.CreateServer($"stale-target-{Guid.NewGuid():N}");
        var source = sourceServer.World;
        var target = targetServer.World;
        WorldBlockEntities.AddEmptyChunk(source);
        WorldBlockEntities.AddEmptyChunk(target);
        var item = Drop(source, 0, new VectorD(12.5, 70, 4.5));

        // A mob and a hopper of the source found the item, which then goes through to the target.
        await item.TeleportAsync(target, new VectorD(12.5, 70, 4.5));

        // What they found is gone from their level: neither takes it, and the target's hopper takes it whole.
        var sourceHopper = new ItemAccessOver(new Container(5));
        var targetHopper = new ItemAccessOver(new Container(5));
        Assert.Null(item.TryTake(source, 1, _ => true));
        Assert.False(await Hoppers.TakeInAsync(sourceHopper.Access, item, source, HopperMin, HopperMax));
        Assert.True(await Hoppers.TakeInAsync(targetHopper.Access, item, target, HopperMin, HopperMax));
        Assert.Equal(1, Diamonds(targetHopper.Items));
        Assert.Equal(0, Diamonds(sourceHopper.Items));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PluginCodeShowingAnArrivalCanSendAnotherEntityOn()
    {
        await using var firstServer = TestServerFactory.CreateServer($"relay-first-{Guid.NewGuid():N}");
        await using var secondServer = TestServerFactory.CreateServer($"relay-second-{Guid.NewGuid():N}");
        await using var thirdServer = TestServerFactory.CreateServer($"relay-third-{Guid.NewGuid():N}");
        var first = firstServer.World;
        var second = secondServer.World;
        var third = thirdServer.World;
        WorldBlockEntities.AddEmptyChunk(first);
        WorldBlockEntities.AddEmptyChunk(second);
        WorldBlockEntities.AddEmptyChunk(third);
        var passedOn = new Cow { Level = second, EntityId = 711_001, Position = Centre };
        Assert.True(second.TryAddEntity(passedOn));
        var relay = new RelayCow(passedOn, third) { Level = first, EntityId = 711_000, Position = Centre };
        Assert.True(first.TryAddEntity(relay));

        // Spawning the relay where it arrives, a plugin's override sends the other cow on from there at once.
        await relay.TeleportAsync(second, Centre);
        await relay.Relayed!.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Same(third, passedOn.Level);
        Assert.Same(passedOn, third.GetRegionForLocation(Centre)!.Entities[passedOn.EntityId]);
        Assert.False(second.GetRegionForLocation(Centre)!.Entities.ContainsKey(passedOn.EntityId));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ATravellerNeitherLevelTakesInIsNeverLost()
    {
        await using var sourceServer = TestServerFactory.CreateServer($"refused-source-{Guid.NewGuid():N}");
        await using var targetServer = TestServerFactory.CreateServer($"refused-target-{Guid.NewGuid():N}");
        var source = sourceServer.World;
        var target = targetServer.World;
        const int chunk = 64;
        var position = new VectorD(chunk * 16 + 8.5, 70, chunk * 16 + 8.5);
        WorldBlockEntities.AddEmptyChunk(source, chunk, chunk);
        WorldBlockEntities.AddEmptyChunk(target);
        await source.GetChunkAsync(chunk, chunk, scheduleGeneration: false);
        var item = Drop(source, 0, position);

        // The target already has an entity with the item's UUID, so it refuses the item. While the item waits to get
        // in, the source unloads the chunks nobody needs: the one the item left is kept for its way back.
        Assert.True(target.TryAddEntity(Twin(item, target, Centre)));
        source.LevelData.GameTime = 20 * 30;
        var refused = await TravelWhileTargetBusyAsync(item, target, Centre, source.ManageChunksAsync);
        Assert.IsType<InvalidOperationException>(refused);
        Assert.Same(source, item.Level);
        Assert.Same(item, source.GetRegionForLocation(position)!.Entities[item.EntityId]);
        Assert.NotNull(LoadedChunk(source, chunk));

        // Refused by the source too (an entity with its UUID came meanwhile), it's kept in the chunk it left, to be
        // saved and spawned again, rather than dropped.
        refused = await TravelWhileTargetBusyAsync(item, target, Centre, () =>
        {
            Assert.True(source.TryAddEntity(Twin(item, source, position)));
            return Task.CompletedTask;
        });
        Assert.IsType<InvalidOperationException>(refused);
        Assert.Contains(LoadedChunk(source, chunk)!.PendingEntities,
            kept => kept.Type == "minecraft:item" && kept.Position == position);
        Assert.Null(item.TryTake(source, 1, _ => true));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AnEntityRemovedWhileTravellingNeverArrives()
    {
        await using var sourceServer = TestServerFactory.CreateServer($"transit-source-{Guid.NewGuid():N}");
        await using var targetServer = TestServerFactory.CreateServer($"transit-target-{Guid.NewGuid():N}");
        var source = sourceServer.World;
        var target = targetServer.World;
        WorldBlockEntities.AddEmptyChunk(source);
        WorldBlockEntities.AddEmptyChunk(target);

        // An item, and an orb, which unlike items and mobs has no removed flag of its own to stop it.
        Entity[] travellers =
        [
            Drop(source, 0, Centre),
            new ExperienceOrb { Level = source, EntityId = 712_000, Position = Centre }
        ];
        Assert.True(source.TryAddEntity(travellers[1]));

        foreach (var traveller in travellers)
        {
            // The target is busy when the entity gets there, and removes it before letting it in.
            var removal = Task.FromResult(false);
            var refused = await TravelWhileTargetBusyAsync(
                traveller,
                target,
                Centre,
                meanwhile: () => Task.CompletedTask,
                underTargetLock: () => removal = target.DestroyEntityAsync(traveller).AsTask());

            Assert.True(removal.IsCompletedSuccessfully);
            Assert.True(await removal);
            Assert.IsType<InvalidOperationException>(refused);
            Assert.DoesNotContain(traveller, source.GetNonPlayerEntitiesInRange(Centre, 8));
            Assert.DoesNotContain(traveller, target.GetNonPlayerEntitiesInRange(Centre, 8));
        }
    }

    // Teleports an entity while the target's transfer lock is held elsewhere. Once the entity has left its level and
    // waits to get in, runs meanwhile, then underTargetLock on the thread holding the target's lock, which then lets
    // the entity in. Returns what the teleport threw, if anything.
    private static async Task<Exception?> TravelWhileTargetBusyAsync(
        Entity traveller, World target, VectorD position, Func<Task> meanwhile, Action? underTargetLock = null)
    {
        using var busy = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = OnOwnThread(() =>
        {
            using (target.TransferLock.Enter())
            {
                busy.Set();
                release.Wait(TimeSpan.FromSeconds(10));
                underTargetLock?.Invoke();
            }
        });
        busy.Wait(TimeSpan.FromSeconds(10));

        var travel = Task.Run(() => traveller.TeleportAsync(target, position).AsTask());
        try
        {
            Assert.True(SpinWait.SpinUntil(() => traveller.Level == target, TimeSpan.FromSeconds(10)));
            await meanwhile();
        }
        finally
        {
            release.Set();
        }

        await holder.WaitAsync(TimeSpan.FromSeconds(10));
        return await Record.ExceptionAsync(() => travel.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    private static ItemEntity Twin(ItemEntity item, World level, VectorD position)
    {
        var twin = new ItemEntity
        {
            Level = level,
            EntityId = item.EntityId + 1_000,
            Position = position,
            Item = new ItemStack(ItemsRegistry.Stone)
        };
        twin.Uuid = item.Uuid;
        return twin;
    }

    private static Chunk? LoadedChunk(World level, int chunk)
    {
        var local = chunk & (Region.CubicRegionSize - 1);
        var region = (Region)level.GetRegionForChunk(chunk, chunk)!;
        return region.GetLoadedChunk(local, local) as Chunk;
    }

    private sealed record Side(World World, Entities.Player Player, ItemAccessOver Hopper);

    // What the levels' threads did: handoffs that succeeded and failed, and those that left while the destination
    // was ticking.
    private sealed class Traffic
    {
        private int handedOff;
        private int failed;
        private int overlapping;

        public int Failed => Volatile.Read(ref this.failed);

        public int Overlapping => Volatile.Read(ref this.overlapping);

        public bool Enough => this.Overlapping >= OverlappingHandoffs && Volatile.Read(ref this.handedOff) >= ItemCount;

        public void Record(bool succeeded, bool destinationTicking)
        {
            if (!succeeded)
            {
                Interlocked.Increment(ref this.failed);
                return;
            }

            Interlocked.Increment(ref this.handedOff);
            if (destinationTicking)
                Interlocked.Increment(ref this.overlapping);
        }
    }

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

    private static async Task RunLevelAsync(Side side, Side other, Traffic traffic)
    {
        for (var round = 0; round < MaxRounds && !traffic.Enough; round++)
        {
            var parity = round % 2;
            side.World.EnqueueEntityAction(() => SendHalfThroughAsync(side.World, other.World, parity, traffic));
            await side.World.DoWorldTickAsync();
            await side.Player.PickupNearbyItemsAsync();

            foreach (var item in ItemsIn(side.World).Where(item => item.Position.X >= HopperX))
                await Hoppers.TakeInAsync(side.Hopper.Access, item, side.World, HopperMin, HopperMax);
        }
    }

    // Run in the level's tick, as a portal sends entities through: they arrive with the destination's next tick.
    private static async ValueTask SendHalfThroughAsync(World level, World destination, int parity, Traffic traffic)
    {
        foreach (var item in ItemsIn(level).Where(item => item.Position.X < HopperX && item.EntityId % 2 == parity))
        {
            var ticking = destination.CurrentTickId != 0;
            try
            {
                await item.TeleportAsync(destination, Spot(item.EntityId + parity));
                traffic.Record(succeeded: true, ticking);
            }
            catch (InvalidOperationException)
            {
                traffic.Record(succeeded: false, ticking);
            }
        }
    }

    private static void ForEachTogether(List<ItemEntity> items, Barrier gate, Action<ItemEntity> take)
    {
        foreach (var item in items)
        {
            Assert.True(gate.SignalAndWait(TimeSpan.FromSeconds(10)));
            take(item);
        }
    }

    private static void Pull(ItemAccessOver hopper, ItemEntity item, World world) =>
        Hoppers.TakeInAsync(hopper.Access, item, world, HopperMin, HopperMax).AsTask().GetAwaiter().GetResult();

    // What a mob picking up its equipment does.
    private static void Equip(List<ItemStack> equipped, ItemEntity item, World world)
    {
        if (item.TryTake(world, 1, _ => true) is not { } taken)
            return;

        equipped.Add(taken.Stack);
        if (taken.Emptied)
            item.RemoveAsync().AsTask().GetAwaiter().GetResult();
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

    // A cow whose spawning, like a plugin entity's override might, sends another entity on to a third level at once.
    private sealed class RelayCow(Entity next, World onwards) : Cow
    {
        public Task? Relayed { get; private set; }

        public override void SpawnEntity(Velocity? velocity = null, int additionalData = 0)
        {
            base.SpawnEntity(velocity, additionalData);
            this.Relayed ??= next.TeleportAsync(onwards, Centre).AsTask();
        }
    }

    // A hopper's slots, reached as a hopper reaches the container it fills.
    private sealed class ItemAccessOver(Container items)
    {
        private readonly object gate = new();

        public Container Items => items;

        public IItemAccess Access => new Slots(this);

        private sealed class Slots(ItemAccessOver owner) : IItemAccess
        {
            public int SlotCount => owner.Items.Size;

            public ItemStack? GetItem(int slot) => owner.Items.GetItem(slot);

            public ItemStack? Insert(int slot, ItemStack stack, bool simulate = false)
            {
                lock (owner.gate)
                {
                    var held = owner.Items.GetItem(slot);
                    if (held is not null && held != stack)
                        return stack;

                    var moved = Math.Min(stack.MaxStackSize - (held?.Count ?? 0), stack.Count);
                    if (moved <= 0)
                        return stack;

                    if (!simulate)
                        owner.Items.SetItem(slot, new ItemStack(stack, (held?.Count ?? 0) + moved));

                    var left = stack.Count - moved;
                    return left > 0 ? new ItemStack(stack, left) : null;
                }
            }

            public ItemStack? Extract(int slot, int count, bool simulate = false) => null;
        }
    }
}
