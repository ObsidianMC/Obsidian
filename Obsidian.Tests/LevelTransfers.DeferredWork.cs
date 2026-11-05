using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.Plugins;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// What runs in the background for ticks that can't wait (see <c>DeferredWork</c>), and the shutdown around it: none of
/// it outlives what it saves, piles up, hides a failure, or hangs a stop.
/// </summary>
public sealed partial class LevelTransfers
{
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task StoppingTheServerFromAPluginChangeLeavesThePluginsLoadedInsteadOfHanging()
    {
        await using var context = TestServerFactory.CreateServer($"stop-plugin-change-{Guid.NewGuid():N}");
        var plugins = context.Server.PluginManager;

        // A plugin's load or ready callback awaits /stop: the change it runs in holds the plugins until it returns.
        var stopped = await Task.Run(async () =>
        {
            var changes = (SemaphoreSlim)typeof(PluginManager)
                .GetField("changes", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(plugins)!;
            var changing = (AsyncLocal<PluginManager?>)typeof(PluginManager)
                .GetField("changing", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetValue(null)!;

            await changes.WaitAsync();
            try
            {
                changing.Value = plugins;
                return await Record.ExceptionAsync(() => context.Server.StopAsync().WaitAsync(TickWait));
            }
            finally
            {
                changing.Value = null;
                changes.Release();
            }
        });

        // The stop went on without disposing the plugins, and reports that, as a change from a callback is refused.
        Assert.IsType<InvalidOperationException>(stopped);
        Assert.True(context.UserCache.Saved);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ASaveOfEverythingAskedForOnceTheServerStoppedIsNotReportedAsSaved()
    {
        await using var context = TestServerFactory.CreateServer($"save-after-stop-{Guid.NewGuid():N}");
        var reported = 0;
        context.Server.SaveCompleted += (_, _) => Interlocked.Increment(ref reported);

        await context.Server.StopAsync().WaitAsync(TickWait);

        Assert.False(await context.Server.SaveEverythingAsync(autosave: false));
        Assert.Equal(0, Volatile.Read(ref reported));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ASaveOfEverythingFailsWithASaveATickStartedInTheBackground()
    {
        await using var context = TestServerFactory.CreateServer($"deferred-failure-{Guid.NewGuid():N}");
        Exception? reported = null;
        context.Server.SaveCompleted += (_, failure) => reported = failure;

        // A save a tick left to run in the background fails, as a full disk would make it.
        var failure = new IOException("The disk is full.");
        Assert.NotNull(context.World.DeferredWork.TryStart(() => Task.FromException(failure)));

        Assert.False(await context.Server.SaveEverythingAsync(autosave: false));
        Assert.Same(failure, Assert.IsType<AggregateException>(reported).Flatten().InnerException);

        // Reported once: the next save of everything succeeds.
        Assert.True(await context.Server.SaveEverythingAsync(autosave: false));
        Assert.Null(reported);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task DisposingADimensionWaitsForTheSaveATickStartedForIt()
    {
        await using var context = TestServerFactory.CreateServer($"dimension-save-dispose-{Guid.NewGuid():N}");
        var world = context.World;
        var nether = AddNether(context);
        AddEmptyChunk(nether);
        var other = (await PrepareAsync(context, "Other")).Player;

        // A save of the world waits for the world to be between ticks (held here), holding the world's save lock.
        using var holding = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Run(() => world.TryRunBetweenTicksAsync(
            () =>
            {
                holding.Set();
                release.Wait(TickWait);
            },
            TickWait));
        Assert.True(holding.Wait(TickWait));
        var worldSave = Task.Run(world.FlushAsync);
        await Task.Delay(StaysBlocked);

        // A player's tick saves the nether, which runs in the background and waits for the world's save lock to write
        // the nether's state.
        await other.GrantAdvancementAsync(async () =>
        {
            await nether.SaveAsync();
            return false;
        });

        // Disposing the nether waits for that save rather than let it write once it's disposed.
        var disposal = nether.DisposeAsync().AsTask();
        await Task.Delay(StaysBlocked);
        var disposedEarly = disposal.IsCompleted;

        release.Set();
        Assert.True(await holder.WaitAsync(TickWait));
        await worldSave.WaitAsync(TickWait);
        await disposal.WaitAsync(TickWait);

        Assert.False(disposedEarly, "The nether was disposed while a save a tick started for it was waiting.");
        Assert.True(File.Exists(nether.StatePath));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task SavesAskedForAgainWhileOneRunsRunOnceMoreAfterIt()
    {
        var deferred = new DeferredWork(NullLogger.Instance);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runs = 0;
        Func<Task> save = async () =>
        {
            Interlocked.Increment(ref runs);
            await release.Task;
        };

        // A plugin saves a level every tick while its first save waits for the ticks.
        Assert.NotNull(deferred.TryStart(save, target: "level"));
        await WaitUntilAsync(() => Volatile.Read(ref runs) == 1);
        for (var tick = 0; tick < 100; tick++)
            Assert.NotNull(deferred.TryStart(save, target: "level"));

        // The hundred requests made during the first save run as one more, after it.
        release.TrySetResult();
        await deferred.CloseAndDrainAsync().WaitAsync(TickWait);
        Assert.Equal(2, Volatile.Read(ref runs));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task DisposingAWorldFromItsOwnTickIsRefused()
    {
        await using var context = TestServerFactory.CreateServer($"dispose-from-tick-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);

        // A plugin in the world's tick saves its regions, which waits in the background for the tick, then disposes
        // the world, which would wait for that save, and so for the tick.
        Exception? disposal = null;
        world.EnqueueEntityAction(async () =>
        {
            await world.FlushRegionsAsync();
            disposal = await Record.ExceptionAsync(() => world.DisposeAsync().AsTask());
        });
        await world.DoWorldTickAsync().WaitAsync(TickWait);

        Assert.IsType<InvalidOperationException>(disposal);
    }
}
