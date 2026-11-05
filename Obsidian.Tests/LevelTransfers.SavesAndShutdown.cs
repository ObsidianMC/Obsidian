using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
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
