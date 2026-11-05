using Obsidian.API;
using Obsidian.API.Commands;
using Obsidian.Commands.Framework;
using Obsidian.Commands.Modules;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
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
    public async Task TravelPickupsAndSavesDuringParallelLevelTicksNeitherDuplicateNorLoseItems()
    {
        await using var context = TestServerFactory.CreateServer($"parallel-traffic-{Guid.NewGuid():N}");
        context.World.Configuration.ParallelLevelTicks = true;
        var nether = AddNether(context);
        var overworld = await PrepareAsync(context, "Overworld");
        var netherSide = await PrepareAsync(context, "Nether", nether);
        for (var index = 0; index < ItemCount; index++)
            Drop(overworld.World, index, Spot(index));

        // The world saves over and over while its levels tick.
        using var stopSaving = new CancellationTokenSource();
        var saves = Task.Run(() => SaveUntilStoppedAsync(context.World, stopSaving.Token));

        // In each level's tick, half its items go through to the other level, as through a portal, while the other
        // level's player picks items up and its hopper pulls them in.
        var traffic = new Traffic();
        for (var round = 0; round < MaxRounds && !traffic.Enough; round++)
        {
            QueueTraffic(overworld, netherSide, round % 2, traffic);
            QueueTraffic(netherSide, overworld, round % 2, traffic);
            await context.World.DoWorldTickAsync().WaitAsync(TimeSpan.FromSeconds(20));
        }

        await stopSaving.CancelAsync();
        await saves.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(0, traffic.Failed);
        Assert.True(traffic.Overlapping >= OverlappingHandoffs, $"Only {traffic.Overlapping} overlapping handoffs.");
        AssertEveryDiamondOnce(overworld, netherSide);
    }

    // Queues a round of the level's item traffic, run in its next tick.
    private static void QueueTraffic(Side side, Side other, int parity, Traffic traffic)
    {
        side.World.EnqueueEntityAction(() => SendHalfThroughAsync(side.World, other.World, parity, traffic));
        side.World.EnqueueEntityAction(async () =>
        {
            await side.Player.PickupNearbyItemsAsync();
            foreach (var item in ItemsIn(side.World).Where(item => item.Position.X >= HopperX))
                await Hoppers.TakeInAsync(side.Hopper.Access, item, side.World, HopperMin, HopperMax, HopperBlock);
        });
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
