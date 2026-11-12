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
        // nether's tick saves the player: each holds the gate the other waits for, so both saves are refused.
        var overworldHolds = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var netherTicks = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? netherSave = null;
        world.EnqueueEntityAction(async () => await player.GrantAdvancementAsync(async () =>
        {
            overworldHolds.TrySetResult();
            await netherTicks.Task.WaitAsync(OverlapWait);
            netherSave = await Record.ExceptionAsync(() => nether.SaveAsync());
            return false;
        }));

        Exception? playerSave = null;
        Exception? tick = null;
        nether.EnqueueEntityAction(async () =>
        {
            netherTicks.TrySetResult();
            await overworldHolds.Task.WaitAsync(OverlapWait);
            playerSave = await Record.ExceptionAsync(() => player.SaveAsync());

            // Ticking a level from a tick is refused too.
            tick = await Record.ExceptionAsync(() => world.DoWorldTickAsync());
        });

        await world.DoWorldTickAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsType<InvalidOperationException>(netherSave);
        Assert.IsType<InvalidOperationException>(playerSave);
        Assert.IsType<InvalidOperationException>(tick);

        // Outside the ticks, both save.
        await nether.SaveAsync();
        await player.SaveAsync();
        Assert.True(File.Exists(nether.StatePath));
        Assert.True(File.Exists(world.GetPlayerDataPath(player.Uuid)));
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
