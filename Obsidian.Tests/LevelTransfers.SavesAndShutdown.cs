using Obsidian.Plugins;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
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
}
