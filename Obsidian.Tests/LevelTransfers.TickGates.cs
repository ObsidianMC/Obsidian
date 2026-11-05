using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Tests.Fakes;
using Obsidian.Utilities;
using Obsidian.WorldData;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// A flow inside a level's tick, or holding a player's tick gate, never waits for a level's simulation gate or another
/// player's tick gate (see <c>EntityTransferLock</c>): what would is refused, or runs in the background, tracked so that
/// shutdown and disposal wait for it. These check the ways in that would otherwise deadlock or write too late.
/// </summary>
public sealed partial class LevelTransfers
{
    private static readonly TimeSpan TickWait = TimeSpan.FromSeconds(10);

    // How long something blocked on purpose is watched to make sure it stays blocked.
    private static readonly TimeSpan StaysBlocked = TimeSpan.FromMilliseconds(300);

    private const string RootAdvancement = "minecraft:story/root";

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PlayersTickingInTwoLevelsAtOnceNeitherGrantToNorRemoveEachOtherIntoADeadlock()
    {
        await using var context = TestServerFactory.CreateServer($"player-gates-{Guid.NewGuid():N}");
        var world = context.World;
        world.Configuration.ParallelLevelTicks = true;
        var nether = AddNether(context);
        WorldBlockEntities.AddEmptyChunk(world);
        AddEmptyChunk(nether);
        var first = (PlayerBase)await context.Server.AddServerPlayerAsync(Guid.NewGuid(), "First", world);
        var second = (PlayerBase)await context.Server.AddServerPlayerAsync(Guid.NewGuid(), "Second", world);
        await second.TeleportAsync(nether, Centre);

        // In each player's grant, in their own level's tick, a plugin grants the other player an advancement: each
        // would wait for the other's gate. That's refused, while a grant to the player themselves works.
        var grants = new CrossedCalls(world, first, nether, second, (_, other) => GrantRootAsync(other));
        await world.DoWorldTickAsync().WaitAsync(TickWait);

        Assert.IsType<InvalidOperationException>(grants.FirstRefusal);
        Assert.IsType<InvalidOperationException>(grants.SecondRefusal);
        Assert.Null(grants.FirstOwnGrant);
        Assert.Null(grants.SecondOwnGrant);

        // Then each ticks the other through the public tick: refused too.
        var ticks = new CrossedCalls(world, first, nether, second, (_, other) => other.TickAsync().AsTask());
        await world.DoWorldTickAsync().WaitAsync(TickWait);

        Assert.IsType<InvalidOperationException>(ticks.FirstRefusal);
        Assert.IsType<InvalidOperationException>(ticks.SecondRefusal);

        // Then each removes the other: the leaves wait for each other's gate, so they finish after the ticks instead.
        var removals = new CrossedCalls(
            world,
            first,
            nether,
            second,
            (_, other) => context.Server.RemoveServerPlayerAsync(other));
        await world.DoWorldTickAsync().WaitAsync(TickWait);

        Assert.Null(removals.FirstRefusal);
        Assert.Null(removals.SecondRefusal);
        await WaitUntilAsync(() => context.Server.OnlinePlayers.IsEmpty);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ALevelsTickAwaitingItsOwnUnloadOrASaveOfEverythingIsNotStuckOnIt()
    {
        await using var context = TestServerFactory.CreateServer($"tick-coordinators-{Guid.NewGuid():N}");
        var world = context.World;
        var player = (await PrepareAsync(context, "Saved")).Player;
        Assert.True(context.Server.AddPlayer(player));
        await player.LoadAsync();

        var completed = 0;
        context.Server.SaveCompleted += (_, failure) =>
        {
            Assert.Null(failure);
            Interlocked.Increment(ref completed);
        };

        var autosaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Server.SaveStarted += _ => autosaveStarted.TrySetResult();

        // In the player's tick, a plugin unloads the level's region, which waits for the level's tick to end: refused.
        // It then saves everything while an autosave holds the server's save lock and waits for the player's tick:
        // that save goes on once the ticks are over.
        Exception? unload = null;
        Task<bool>? autosave = null;
        bool? saved = null;
        world.EnqueueEntityAction(async () => await player.GrantAdvancementAsync(async () =>
        {
            unload = await Record.ExceptionAsync(() => world.UnloadRegionAsync(0, 0));

            using (ExecutionContext.SuppressFlow())
                autosave = Task.Run(() => context.Server.SaveEverythingAsync(autosave: true));

            await autosaveStarted.Task.WaitAsync(TickWait);
            saved = await context.Server.SaveEverythingAsync(autosave: false).WaitAsync(TickWait);
            return false;
        }));

        await world.DoWorldTickAsync().WaitAsync(TickWait);

        Assert.IsType<InvalidOperationException>(unload);
        Assert.True(saved);
        Assert.True(await autosave!.WaitAsync(TickWait));
        await WaitUntilAsync(() => Volatile.Read(ref completed) == 2);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ATickAwaitingTravelIntoAnotherLevelsUngeneratedChunkGeneratesItThoughThatLevelsGateIsHeld()
    {
        await using var sourceServer = TestServerFactory.CreateServer($"travel-source-{Guid.NewGuid():N}");
        await using var targetServer = TestServerFactory.CreateServer($"travel-target-{Guid.NewGuid():N}");
        var source = sourceServer.World;
        var target = targetServer.World;
        WorldBlockEntities.AddEmptyChunk(source);
        WorldBlockEntities.AddEmptyChunk(target);
        WorldBlockEntities.AddEmptyChunk(target, 1, 0);
        Assert.NotNull(await target.GetChunkAsync(1, 0));
        var cow = new Cow { Level = source, EntityId = 719_000, Position = Centre };
        Assert.True(source.TryAddEntity(cow));

        // The target's tick reaches a chunk management tick, at which its chunk management unloads its idle chunk (not
        // the spawn chunk): that waits at the target's gate, which the target's tick holds until the cow arrives.
        target.LevelData.GameTime = 20 * 30 - 1;
        var targetTicks = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? management = null;
        target.EnqueueEntityAction(async () =>
        {
            using (ExecutionContext.SuppressFlow())
                management = Task.Run(target.ManageChunksAsync);

            targetTicks.TrySetResult();
            await arrived.Task.WaitAsync(TickWait);
        });

        // A plugin in the source's tick, meanwhile, awaits sending the cow to an ungenerated chunk of the target.
        Exception? travel = null;
        var managementWaited = false;
        source.EnqueueEntityAction(async () =>
        {
            await targetTicks.Task.WaitAsync(TickWait);
            var sent = cow.TeleportAsync(target, Ungenerated).AsTask();
            travel = await Record.ExceptionAsync(() => sent.WaitAsync(TimeSpan.FromSeconds(5)));
            managementWaited = management is { IsCompleted: false };
            arrived.TrySetResult();
        });

        var targetTick = Task.Run(target.DoWorldTickAsync);
        await source.DoWorldTickAsync().WaitAsync(TimeSpan.FromSeconds(20));
        await targetTick.WaitAsync(TickWait);

        Assert.Null(travel);
        Assert.True(managementWaited, "The target's chunk management didn't wait at its gate.");
        Assert.True(target.GetLoadedChunk(UngeneratedChunk, UngeneratedChunk) is { IsGenerated: true });
        Assert.Same(cow, target.FindEntity(cow.Uuid));
        await management!.WaitAsync(TickWait);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task SavesAskedForFromATickFinishBeforeTheWorldIsDisposedAndNoneRunsAfter()
    {
        await using var context = TestServerFactory.CreateServer($"deferred-saves-{Guid.NewGuid():N}");
        var world = context.World;
        var saved = (await PrepareAsync(context, "Saved")).Player;
        var other = (await PrepareAsync(context, "Other")).Player;
        await saved.LoadAsync();
        var dataPath = world.GetPlayerDataPath(saved.Uuid);

        // The player is in the middle of a tick (held by the test) when another player's tick saves them: the save
        // waits, in the background, for the player's tick to end.
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = Task.Run(() => saved.GrantAdvancementAsync(async () =>
        {
            holding.TrySetResult();
            await release.Task;
            return false;
        }).AsTask());
        await holding.Task.WaitAsync(TickWait);
        await SaveFromAnotherPlayersTickAsync(saved, other);

        // Disposing the world waits for that save, which writes before the world lets go of its files.
        var disposal = world.DisposeAsync().AsTask();
        await Task.Delay(StaysBlocked);
        var disposedEarly = disposal.IsCompleted;

        release.TrySetResult();
        Assert.False(disposedEarly, "The world was disposed while a save it accepted was waiting.");
        await held.WaitAsync(TickWait);
        await disposal.WaitAsync(TickWait);
        Assert.True(File.Exists(dataPath));

        // Once disposed, a save asked for from a tick writes nothing.
        File.Delete(dataPath);
        await SaveFromAnotherPlayersTickAsync(saved, other);
        await Task.Delay(StaysBlocked);
        Assert.False(File.Exists(dataPath));
    }

    private static Task GrantRootAsync(PlayerBase player) => player.Advancements.GrantAsync(RootAdvancement).AsTask();

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AddingAPlayerAgainFromTheTickThatRemovedThemIsRefusedWhileTheirLeaveWaitsForIt()
    {
        await using var context = TestServerFactory.CreateServer($"rejoin-from-tick-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        var bot = await context.Server.AddServerPlayerAsync(Guid.NewGuid(), "Bot", world);

        // A plugin saves the level as players leave, which waits for the level's tick to end.
        context.EventDispatcher.RegisterEvent(
            null,
            (Func<PlayerLeaveEventArgs, ValueTask>)(async _ => await world.FlushRegionsAsync()),
            Priority.Critical);

        // In the level's tick, another plugin removes the bot and adds it again: adding it would wait for the leave,
        // which waits for that tick, so it's refused.
        Exception? rejoin = null;
        world.EnqueueEntityAction(async () =>
        {
            Assert.True(await context.Server.RemoveServerPlayerAsync(bot));
            var adding = () => context.Server.AddServerPlayerAsync(Guid.NewGuid(), "Bot", world);
            rejoin = await Record.ExceptionAsync(adding);
        });
        await world.DoWorldTickAsync().WaitAsync(TickWait);

        Assert.IsType<InvalidOperationException>(rejoin);

        // Once the ticks are over, the leave finishes, and adding the bot again waits for it.
        var again = await context.Server.AddServerPlayerAsync(Guid.NewGuid(), "Bot", world).WaitAsync(TickWait);
        Assert.True(context.Server.IsPlayerOnline(again.Uuid));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ALeaveWaitsForTheSavesTicksAskedOfThePlayerAndRunsNoneAfter()
    {
        await using var context = TestServerFactory.CreateServer($"leave-deferred-{Guid.NewGuid():N}");
        var world = context.World;
        var leaving = (await PrepareAsync(context, "Leaving")).Player;
        var other = (await PrepareAsync(context, "Other")).Player;
        await leaving.LoadAsync();
        var dataPath = world.GetPlayerDataPath(leaving.Uuid);

        // The leaving player is mid-tick (held by the test) when another player's tick saves them: the save waits in
        // the background.
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = Task.Run(() => leaving.GrantAdvancementAsync(async () =>
        {
            holding.TrySetResult();
            await release.Task;
            return false;
        }).AsTask());
        await holding.Task.WaitAsync(TickWait);
        await SaveFromAnotherPlayersTickAsync(leaving, other);

        // The leave's first step waits for that save, so the leave's own save comes after it.
        var finishing = leaving.FinishDeferredWorkAsync();
        await Task.Delay(StaysBlocked);
        var finishedEarly = finishing.IsCompleted;

        release.TrySetResult();
        Assert.False(finishedEarly, "The leave went on while a save it accepted was waiting.");
        await held.WaitAsync(TickWait);
        await finishing.WaitAsync(TickWait);
        Assert.True(File.Exists(dataPath));

        // From then on a save asked for from a tick writes nothing, so it never overwrites a join again's data.
        File.Delete(dataPath);
        await SaveFromAnotherPlayersTickAsync(leaving, other);
        await Task.Delay(StaysBlocked);
        Assert.False(File.Exists(dataPath));
    }

    // Saves the player from inside the other player's grant, as a plugin's handler of an event in their tick might.
    private static Task SaveFromAnotherPlayersTickAsync(PlayerBase player, PlayerBase other) =>
        other.GrantAdvancementAsync(async () =>
        {
            await player.SaveAsync();
            return false;
        }).AsTask();

    // In each of two levels' ticks, inside a player's grant there, calls something on the other player once both are in
    // their grants, then grants the player themselves an advancement. Records what each call threw.
    private sealed class CrossedCalls
    {
        public CrossedCalls(
            AbstractLevel firstLevel,
            PlayerBase first,
            AbstractLevel secondLevel,
            PlayerBase second,
            Func<PlayerBase, PlayerBase, Task> call)
        {
            var firstIn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondIn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            firstLevel.EnqueueEntityAction(async () => await first.GrantAdvancementAsync(async () =>
            {
                firstIn.TrySetResult();
                await secondIn.Task.WaitAsync(OverlapWait);
                this.FirstRefusal = await Record.ExceptionAsync(() => call(first, second));
                this.FirstOwnGrant = await Record.ExceptionAsync(() => GrantRootAsync(first));
                return false;
            }));

            secondLevel.EnqueueEntityAction(async () => await second.GrantAdvancementAsync(async () =>
            {
                secondIn.TrySetResult();
                await firstIn.Task.WaitAsync(OverlapWait);
                this.SecondRefusal = await Record.ExceptionAsync(() => call(second, first));
                this.SecondOwnGrant = await Record.ExceptionAsync(() => GrantRootAsync(second));
                return false;
            }));
        }

        public Exception? FirstRefusal { get; private set; }

        public Exception? SecondRefusal { get; private set; }

        public Exception? FirstOwnGrant { get; private set; }

        public Exception? SecondOwnGrant { get; private set; }
    }
}
