using Microsoft.Extensions.Logging;
using Obsidian.Entities;
using Obsidian.Plugins;
using Obsidian.Utilities;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Saves and the shutdown around the ticks: what would wait for a tick from inside one is refused, and nothing the
/// shutdown waits for can be waiting for it.
/// </summary>
public sealed partial class LevelTransfers
{
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task SavesFromInsideATickAreRefused()
    {
        await using var context = TestServerFactory.CreateServer($"saves-from-tick-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        var player = (await PrepareAsync(context, "Saved")).Player;

        // A plugin in the level's tick saves, in each of the ways a plugin can.
        Dictionary<string, Func<Task>> saves = new()
        {
            ["the level"] = world.SaveAsync,
            ["its regions"] = world.FlushRegionsAsync,
            ["the world"] = world.FlushAsync,
            ["a player"] = player.SaveAsync,
            ["everything"] = () => context.Server.SaveEverythingAsync(autosave: false)
        };
        Dictionary<string, Exception?> refused = [];
        world.EnqueueEntityAction(async () =>
        {
            foreach (var (name, save) in saves)
                refused[name] = await Record.ExceptionAsync(save);
        });
        await world.DoWorldTickAsync().WaitAsync(TickWait);

        foreach (var (name, failure) in refused)
            Assert.True(failure is InvalidOperationException, $"Saving {name} from a tick wasn't refused: {failure}");
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AStopAwaitedFromAPluginChangeReturnsAtOnceThoughTheShutdownStartedFirst()
    {
        await using var context = TestServerFactory.CreateServer($"stop-plugin-change-{Guid.NewGuid():N}");
        var plugins = context.Server.PluginManager;
        var changes = (SemaphoreSlim)typeof(PluginManager)
            .GetField("changes", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(plugins)!;
        var changing = (AsyncLocal<PluginManager?>)typeof(PluginManager)
            .GetField("changing", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;

        // A plugin's load callback runs, holding the plugins' changes, when the console stops the server: the shutdown
        // will wait for that change as it disposes the plugins.
        await changes.WaitAsync();
        Task shutdown;
        Exception? awaited;
        try
        {
            shutdown = context.Server.StopAsync();

            // The callback then awaits a stop itself, which mustn't wait for the shutdown that waits for it.
            awaited = await Task.Run(async () =>
            {
                changing.Value = plugins;
                return await Record.ExceptionAsync(() => context.Server.StopAsync().WaitAsync(TickWait));
            });
        }
        finally
        {
            changes.Release();
        }

        Assert.Null(awaited);

        // Once the callback returned, the shutdown disposes the plugins and ends.
        await shutdown.WaitAsync(TickWait);
        Assert.True(context.UserCache.Saved);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AStopAwaitedWhileAPluginsDimensionIsCreatedReturnsAtOnce()
    {
        // The world manager's disposal waits for its loop's work: here, a plugin dimension's creation.
        var created = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var context = TestServerFactory.CreateServer(
            $"stop-dimension-creation-{Guid.NewGuid():N}",
            world => new FakeWorldManager(world) { DisposeWaitsFor = created.Task });

        // The dimension's generator, finding its spawn, awaits a stop, which mustn't wait for the shutdown that waits
        // for the creation.
        var awaited = await Task.Run(async () =>
        {
            PluginManager.creatingDimensions.Value = new PluginManager.DimensionPass();
            try
            {
                return await Record.ExceptionAsync(() => context.Server.StopAsync().WaitAsync(TickWait));
            }
            finally
            {
                created.TrySetResult();
            }
        });

        Assert.Null(awaited);
        await context.Server.StopAsync().WaitAsync(TickWait);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task BackgroundWorkWhoseFailureCantBeLoggedStillEndsForTheShutdown()
    {
        // The server's log can't be written (a full disk, say) when work in the background fails.
        var work = new DeferredWork(new ThrowingLogger());
        Assert.True(work.TryStart(() => Task.FromException(new IOException("The save failed."))));

        await work.CloseAndDrainAsync().WaitAsync(TickWait);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task APlayerSentToAnotherLevelFromATickArrivesAndIsSaved()
    {
        await using var context = TestServerFactory.CreateServer($"travel-from-tick-{Guid.NewGuid():N}");
        var world = context.World;
        var nether = AddNether(context);
        WorldBlockEntities.AddEmptyChunk(world);
        AddEmptyChunk(nether);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Pearled", Centre);
        Assert.True(context.Server.AddPlayer(player));
        player.Level = nether;
        Assert.True(nether.TryAddPlayer(player));
        Assert.True(nether.TryAddEntity(player));
        await player.LoadAsync();

        // An ender pearl in the nether's tick lands in the overworld, taking its owner there.
        Exception? travel = null;
        nether.EnqueueEntityAction(async () =>
        {
            travel = await Record.ExceptionAsync(() => player.TeleportAsync(world, Centre).AsTask());
        });
        await nether.DoWorldTickAsync().WaitAsync(TickWait);

        // The travel completes, and its save runs once the tick is over.
        Assert.Null(travel);
        Assert.Same(world, player.Level);
        await WaitUntilAsync(() => File.Exists(world.GetPlayerDataPath(player.Uuid)));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ATriggerFiredInTheLastTickIsGrantedBeforeThePlayersLeave()
    {
        await using var context = TestServerFactory.CreateServer($"last-tick-trigger-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        var bot = (PlayerBase)await context.Server.AddServerPlayerAsync(Guid.NewGuid(), "Bot", world);

        // The server's loop is in its last tick, which the shutdown waits for.
        var lastTick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        typeof(Server)
            .GetField("loopTask", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context.Server, lastTick.Task);

        // In that tick, /stop runs, and then a trigger is fired for the bot from another player's tick: its grant is
        // left to the background.
        var accepted = false;
        TaskCompletionSource? granted = null;
        world.EnqueueEntityAction(async () =>
        {
            _ = context.Server.StopAsync();
            await WaitUntilAsync(() => context.Server.Stopping);
            granted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            accepted = context.Server.BackgroundWork.TryStart(() => bot.GrantAdvancementAsync(() =>
            {
                granted.TrySetResult();
                return ValueTask.FromResult(false);
            }).AsTask());
        });
        await world.DoWorldTickAsync().WaitAsync(TickWait);

        // Once the tick ends, the shutdown grants it before the bot leaves.
        lastTick.TrySetResult();
        await context.Server.StopAsync().WaitAsync(TickWait);

        Assert.True(accepted, "The last tick's trigger was refused.");
        Assert.True(granted!.Task.IsCompletedSuccessfully);
        Assert.True(context.Server.OnlinePlayers.IsEmpty);
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
    public async Task DisposingAWorldFromItsOwnTickIsRefused()
    {
        await using var context = TestServerFactory.CreateServer($"dispose-from-tick-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);

        // A plugin in the world's tick disposes the world, which would wait for that tick.
        Exception? disposal = null;
        world.EnqueueEntityAction(async () =>
        {
            disposal = await Record.ExceptionAsync(() => world.DisposeAsync().AsTask());
        });
        await world.DoWorldTickAsync().WaitAsync(TickWait);

        Assert.IsType<InvalidOperationException>(disposal);
    }

    // A logger whose writes fail, as a log file on a full disk does.
    private sealed class ThrowingLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            throw new IOException("There is not enough space on the disk.");
    }
}
