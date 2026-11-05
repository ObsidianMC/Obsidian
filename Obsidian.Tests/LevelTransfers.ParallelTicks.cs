using Obsidian.API;
using Obsidian.API.Commands;
using Obsidian.API.Inventory;
using Obsidian.Commands.Framework;
using Obsidian.Commands.Modules;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Utilities;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// A world's levels tick at the same time, each on a thread of its own (see <c>World.DoWorldTickAsync</c>). These check
/// that they really do, and that nothing they share is duplicated, lost or deadlocked when they do.
/// </summary>
public sealed partial class LevelTransfers
{
    // How long a level's tick waits for another level's to start before taking it that they don't overlap.
    private static readonly TimeSpan OverlapWait = TimeSpan.FromSeconds(10);

    // How long a level's tick waits to find that a turned off world never starts its dimensions beside it.
    private static readonly TimeSpan NoOverlapWait = TimeSpan.FromMilliseconds(300);

    // How long a travel gets to finish while a level's tracking of the player is held up, before it's let go.
    private static readonly TimeSpan TravelHeadStart = TimeSpan.FromMilliseconds(300);

    // How many items each level's tick sends through and lets a mob take at the same moment, and how many such races
    // run before the traffic stops.
    private const int ContestedPerTick = 8;
    private const int ContestedRaces = 400;

    // The mob's head start is taken away in this many steps of this many spins.
    private const int TakeDelays = 16;
    private const int TakeDelayStep = 200;

    [Theory(Timeout = TestServerFactory.TestTimeout)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AWorldsLevelsTickAtTheSameTimeUnlessTurnedOff(bool parallel)
    {
        await using var context = TestServerFactory.CreateServer($"parallel-ticks-{Guid.NewGuid():N}");
        context.World.Configuration.ParallelLevelTicks = parallel;
        var nether = AddNether(context);

        // Each level's tick waits for the other's to have started.
        var overworld = new TickProbe(context.World);
        var netherProbe = new TickProbe(nether);
        var wait = parallel ? OverlapWait : NoOverlapWait;
        overworld.WaitInTickFor(netherProbe, wait);
        netherProbe.WaitInTickFor(overworld, wait);

        await context.World.DoWorldTickAsync().WaitAsync(TimeSpan.FromSeconds(20));

        // In order, the world's own level ends its tick before its dimensions start theirs.
        Assert.Equal(parallel, overworld.SawOther);
        Assert.True(netherProbe.SawOther);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task SavesRunFromTwoLevelsTicksAtOnceDoNotDeadlockThem()
    {
        await using var context = TestServerFactory.CreateServer($"parallel-saves-{Guid.NewGuid():N}");
        context.World.Configuration.ParallelLevelTicks = true;
        var handler = (CommandHandler)context.Server.CommandHandler;
        handler.RegisterCommandClass<WorldCommandModule>(null);
        var nether = AddNether(context);
        var end = AddDimension(context, "minecraft:the_end");

        // As a command block minecart in each runs /execute in <the other> run setworldspawn: saving a dimension waits
        // for its gate, which the other level's tick holds while that tick waits for this one's.
        var netherProbe = new TickProbe(nether);
        var endProbe = new TickProbe(end);
        netherProbe.WaitInTickFor(endProbe, OverlapWait, () => SetSpawnAsync(context, end, 5));
        endProbe.WaitInTickFor(netherProbe, OverlapWait, () => SetSpawnAsync(context, nether, 7));

        await context.World.DoWorldTickAsync().WaitAsync(TimeSpan.FromSeconds(20));

        Assert.True(netherProbe.SawOther && endProbe.SawOther, "The dimensions' ticks didn't overlap.");
        Assert.Equal(5.5, end.LevelData.SpawnPosition.X);
        Assert.Equal(7.5, nether.LevelData.SpawnPosition.X);

        // The saves ran once the ticks let go of the gates.
        await WaitUntilAsync(() => File.Exists(end.StatePath) && File.Exists(nether.StatePath));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task SavesAwaitedFromAPlayersTickAndAnotherLevelsTickAtOnceDoNotDeadlockThem()
    {
        await using var context = TestServerFactory.CreateServer($"parallel-player-saves-{Guid.NewGuid():N}");
        var world = context.World;
        world.Configuration.ParallelLevelTicks = true;
        var nether = AddNether(context);
        var player = (await PrepareAsync(context, "Saver")).Player;
        await player.LoadAsync();

        // A plugin's callback in the player's grant, in the overworld's tick, saves the nether, while one in the
        // nether's tick saves the player: each holds the gate the other waits for, unless the saves wait for neither.
        var overworldHolds = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var netherTicks = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.EnqueueEntityAction(async () => await player.GrantAdvancementAsync(async () =>
        {
            overworldHolds.TrySetResult();
            await netherTicks.Task.WaitAsync(OverlapWait);
            await nether.SaveAsync();
            return false;
        }));

        Exception? refused = null;
        nether.EnqueueEntityAction(async () =>
        {
            netherTicks.TrySetResult();
            await overworldHolds.Task.WaitAsync(OverlapWait);
            await player.SaveAsync();

            // Ticking a level from a tick can't wait for later, so it's refused.
            refused = await Record.ExceptionAsync(() => world.DoWorldTickAsync());
        });

        await world.DoWorldTickAsync().WaitAsync(TimeSpan.FromSeconds(10));

        // Both saves ran once the ticks let go of the gates.
        Assert.IsType<InvalidOperationException>(refused);
        await WaitUntilAsync(() => File.Exists(nether.StatePath) && File.Exists(world.GetPlayerDataPath(player.Uuid)));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ALevelsTrackingDoesNotUndoThatOfTheLevelItsPlayerWentTo()
    {
        await using var context = TestServerFactory.CreateServer($"parallel-tracking-{Guid.NewGuid():N}");
        var world = context.World;
        var nether = AddNether(context);
        WorldBlockEntities.AddEmptyChunk(world);
        AddEmptyChunk(nether);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Tracked", Centre);
        Assert.True(context.Server.AddPlayer(player));
        Assert.True(world.TryAddPlayer(player));
        Assert.True(world.TryAddEntity(player));
        player.LoadedChunks.Add(NumericsHelper.IntsToLong(0, 0));

        var overworldCow = new SpawnGatedCow { Level = world, EntityId = 714_000, Position = Centre };
        var netherCow = new Cow { Level = nether, EntityId = 714_001, Position = Centre };
        Assert.True(world.TryAddEntity(overworldCow));
        Assert.True(nether.TryAddEntity(netherCow));

        // The overworld's tracking pass is about to show the player its cow when an ender pearl in the nether's tick
        // takes the player there, where the nether's tracking shows them the nether's cow.
        var tracking = Task.Run(() => ((IPlayer)player).SynchronizeTrackedEntitiesAsync(world).AsTask());
        Assert.True(overworldCow.Entered.Wait(TimeSpan.FromSeconds(10)));
        var travel = Task.Run(() => TravelAndTrackAsync(player, nether));
        await Task.WhenAny(travel, Task.Delay(TravelHeadStart));
        overworldCow.Release.Set();
        await Task.WhenAll(tracking, travel).WaitAsync(TimeSpan.FromSeconds(10));
        AssertTracksOnly(player, netherCow);

        // A pass of the overworld from a snapshot taken before the travel leaves the player to the nether too.
        await ((IPlayer)player).SynchronizeTrackedEntitiesAsync(world);
        AssertTracksOnly(player, netherCow);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task TravelPickupsAndSavesDuringParallelLevelTicksNeitherDuplicateNorLoseItems()
    {
        await using var context = TestServerFactory.CreateServer($"parallel-traffic-{Guid.NewGuid():N}");
        context.World.Configuration.ParallelLevelTicks = true;
        var nether = AddNether(context);
        var overworld = await PrepareAsync(context, "Overworld");
        var netherSide = await PrepareAsync(context, "Nether", nether);
        await overworld.Player.LoadAsync();
        await netherSide.Player.LoadAsync();
        for (var index = 0; index < ItemCount; index++)
            Drop(overworld.World, index, Spot(index));

        // The world saves over and over while its levels tick.
        using var stopSaving = new CancellationTokenSource();
        var saves = Task.Run(() => SaveUntilStoppedAsync(context.World, stopSaving.Token));

        // In each level's tick, half its items go through to the other level, as through a portal, while the other
        // level's player picks items up and its hopper pulls them in. A few more are sent through and taken by a mob
        // at the same moment.
        var traffic = new Traffic();
        var contest = new Contest();
        try
        {
            for (var round = 0; round < MaxRounds && !(traffic.Enough && contest.Enough); round++)
            {
                QueueTraffic(overworld, netherSide, round % 2, traffic, contest);
                QueueTraffic(netherSide, overworld, round % 2, traffic, contest);
                await context.World.DoWorldTickAsync().WaitAsync(TimeSpan.FromSeconds(20));
            }
        }
        finally
        {
            // Stopped however the ticks ended, so it never keeps saving a disposed world.
            await stopSaving.CancelAsync();
            await saves.WaitAsync(TimeSpan.FromSeconds(20));
        }

        Assert.Equal(0, traffic.Failed);
        Assert.True(traffic.Overlapping >= OverlappingHandoffs, $"Only {traffic.Overlapping} overlapping handoffs.");
        Assert.True(contest.Enough, $"Only {contest.Sent} contested items sent and {contest.Taken} taken.");
        AssertEveryDiamondOnce(overworld, netherSide);

        // Saved and read back, every diamond is still there once.
        Assert.Equal(ItemCount, await SavedDiamondsAsync(context, overworld, netherSide));
    }

    // Queues a round of the level's item traffic, run in its next tick.
    private static void QueueTraffic(Side side, Side other, int parity, Traffic traffic, Contest contest)
    {
        side.World.EnqueueEntityAction(() => SendHalfThroughAsync(side.World, other.World, parity, traffic));
        side.World.EnqueueEntityAction(async () =>
        {
            await side.Player.PickupNearbyItemsAsync();
            foreach (var item in ItemsIn(side.World).Where(item => item.Position.X >= HopperX))
                await Hoppers.TakeInAsync(side.Hopper.Access, item, side.World, HopperMin, HopperMax, HopperBlock);
        });
        side.World.EnqueueEntityAction(() => ContendAsync(side, other.World, 1 - parity, contest));
    }

    // Run in the level's tick: a few of its items, of those not sent through this round, are each sent through to
    // the other level and taken by a mob at the same moment. Only the item's level's transfer lock keeps both from
    // having it.
    private static async ValueTask ContendAsync(Side side, AbstractLevel destination, int parity, Contest contest)
    {
        var items = ItemsIn(side.World)
            .Where(item => item.Position.X < HopperX && item.EntityId % 2 == parity)
            .Take(ContestedPerTick)
            .ToList();

        using var gate = new Barrier(2);
        await Task.WhenAll(
            OnOwnThread(() => ForEachTogether(items, gate, item => SendContested(item, destination, contest))),
            OnOwnThread(() => ForEachTogether(items, gate, item => TakeContested(side, item, contest))));
    }

    private static void SendContested(ItemEntity item, AbstractLevel destination, Contest contest)
    {
        try
        {
            item.TeleportAsync(destination, Spot(item.EntityId)).AsTask().GetAwaiter().GetResult();
            contest.RecordSent();
        }
        catch (InvalidOperationException)
        {
            // The mob took it first.
        }
    }

    // The mob starts a little later each time, through a range of delays: the portal takes longer to reach the item's
    // lock, so some of the races then reach it at the same moment. It drops what it took again at once, as a new item,
    // so that the races never run out of items.
    private static void TakeContested(Side side, ItemEntity item, Contest contest)
    {
        Thread.SpinWait(item.EntityId % TakeDelays * TakeDelayStep);

        var taken = new List<ItemStack>();
        Equip(taken, item, side.World);
        foreach (var stack in taken)
        {
            DropAgain(side.World, stack);
            contest.RecordTaken();
        }
    }

    private static void DropAgain(AbstractLevel level, ItemStack stack)
    {
        var entityId = Server.GetNextEntityId();
        var item = new ItemEntity
        {
            Level = level,
            EntityId = entityId,
            Position = Spot(entityId),
            Item = stack,
            CanPickup = true
        };
        Assert.True(level.TryAddEntity(item));
    }

    // Saves the world and the players, then reads back what was written: the regions' items and the players'
    // inventories. The hoppers are the test's own, kept only in memory, so they're counted as they are.
    private static async Task<int> SavedDiamondsAsync(RealServerContext context, params Side[] sides)
    {
        var diamonds = 0;
        foreach (var side in sides)
        {
            await side.Player.SaveAsync();
            var reloaded = TestServerFactory.CreateConnectedPlayer(
                context.Server, context.World, side.Player.Username, uuid: side.Player.Uuid);
            await reloaded.LoadAsync();
            diamonds += Diamonds(reloaded.Inventory) + Diamonds(side.Hopper.Items);
        }

        // Disposing the levels closes their region files, which are read back apart from them.
        await context.World.FlushAsync();
        await context.World.DisposeAsync();
        foreach (var side in sides)
            diamonds += await SavedItemDiamondsAsync(side.World);

        return diamonds;
    }

    private static async Task<int> SavedItemDiamondsAsync(AbstractLevel level)
    {
        var loaded = new List<Chunk>();
        await using var region = new Region(0, 0, level.FolderPath, WorldLayout.RegionFolder,
            minY: level.MinY, height: level.Height)
        {
            EntitiesLoaded = loaded.Add
        };
        await region.InitAsync();
        await region.GetChunkAsync(0, 0);

        var items = loaded
            .SelectMany(chunk => chunk.PendingEntities)
            .Select(pending => EntityNbt.Load(EntityNbt.ToNbt(pending), null!))
            .OfType<ItemEntity>();
        return Diamonds(items.Select(item => item.Item));
    }

    private static async Task SaveUntilStoppedAsync(World world, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
            await world.FlushAsync();
    }

    // Sets the level's spawn the way a command block minecart in another level's tick does.
    private static Task SetSpawnAsync(RealServerContext context, AbstractLevel level, int x)
    {
        var sender = new CommandSender(CommandIssuers.Console, player: null);
        var command = new CommandContext($"/setworldspawn {x} 64 0", sender, null, context.Server)
        {
            Level = level
        };

        return context.Server.CommandHandler.ProcessCommand(command);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the condition.");
            await Task.Delay(20);
        }
    }

    // Takes the player to the level's centre, and tracks them there once the client has the chunk.
    private static async Task TravelAndTrackAsync(Entities.Player player, AbstractLevel level)
    {
        await player.TeleportAsync(level, Centre);
        player.LoadedChunks.Add(NumericsHelper.IntsToLong(0, 0));
        await ((IPlayer)player).SynchronizeTrackedEntitiesAsync(level);
    }

    private static void AssertTracksOnly(Entities.Player player, Entity entity)
    {
        var tracked = player.TrackedEntities.Keys.Where(id => id != player.EntityId);
        Assert.Equal([entity.EntityId], tracked);
    }

    // A cow whose first spawn packet waits for the test to release it, holding up the tracking that shows it.
    private sealed class SpawnGatedCow : Cow
    {
        public ManualResetEventSlim Entered { get; } = new();

        public ManualResetEventSlim Release { get; } = new();

        internal override IClientboundPacket CreateSpawnPacket(Velocity? velocity = null, int additionalData = 0)
        {
            if (!this.Entered.IsSet)
            {
                this.Entered.Set();
                this.Release.Wait(TimeSpan.FromSeconds(10));
            }

            return base.CreateSpawnPacket(velocity, additionalData);
        }
    }

    // The races between a portal and a mob over the same item: how many each side won. The mob, quicker to reach the
    // item's lock, wins most.
    private sealed class Contest
    {
        private int sent;
        private int taken;

        public int Sent => Volatile.Read(ref this.sent);

        public int Taken => Volatile.Read(ref this.taken);

        public bool Enough => this.Sent + this.Taken >= ContestedRaces && this.Sent > 0 && this.Taken > 0;

        public void RecordSent() => Interlocked.Increment(ref this.sent);

        public void RecordTaken() => Interlocked.Increment(ref this.taken);
    }

    // A level's tick that signals it started, then waits for another level's to start, then does its work.
    private sealed class TickProbe(AbstractLevel level)
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private bool sawOther;

        // Whether the other level's tick had started while this one waited for it.
        public bool SawOther => Volatile.Read(ref this.sawOther);

        public void WaitInTickFor(TickProbe other, TimeSpan wait, Func<Task>? then = null)
        {
            level.EnqueueEntityAction(async () =>
            {
                this.started.TrySetResult();
                var first = await Task.WhenAny(other.started.Task, Task.Delay(wait));
                Volatile.Write(ref this.sawOther, first == other.started.Task);

                if (then is not null)
                    await then();
            });
        }
    }
}
