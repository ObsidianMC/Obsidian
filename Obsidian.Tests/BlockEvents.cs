using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Registries;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData.BlockNotifications;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class BlockEvents
{
    [Fact]
    public async Task DrainAllowsTickLockedEnqueueFromAnotherThread()
    {
        await using var context = TestServerFactory.CreateServer($"block-events-{Guid.NewGuid():N}");
        var world = context.World;
        await BlockUpdates.AddChunksAsync(world);
        var listener = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Listener");
        world.Players.TryAdd(listener.Uuid, listener);
        var position = new Vector(0, 72, 0);
        world.BlockUpdates.SetBlock(position, BlocksRegistry.Chest,
            BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
        var completed = false;
        world.QueueBlockEvent(position, BlockIds.Chest, 1, 0, async () =>
        {
            // A block callback can await work that takes the tick lock and queues another event.
            // Holding either lock across the callback would deadlock this worker.
            await Task.Run(() =>
            {
                lock (world.Fluids.TickLock)
                {
                    world.QueueBlockEvent(position, BlockIds.Chest, 1, 0, () =>
                    {
                        completed = true;
                        return ValueTask.FromResult(true);
                    });
                }
            }).WaitAsync(TimeSpan.FromSeconds(10));

            return true;
        });

        await world.ProcessBlockEventsAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(completed);
        Assert.Equal(0, world.PendingBlockEvents);
    }
}
