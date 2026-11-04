using Obsidian.Tests.Fakes;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Guards the server's stop: what it releases even when its final save fails.
/// </summary>
public sealed class ServerShutdown
{
    [Fact(DisplayName = "A failed final save still disposes the worlds, saves the user cache and fails the stop", Timeout = TestServerFactory.TestTimeout)]
    public async Task FailedFinalSaveStillFinishesTheStop()
    {
        var saveFailure = new IOException("The disk is full.");
        FakeWorldManager? manager = null;
        await using var context = TestServerFactory.CreateServer(
            $"shutdown-{Guid.NewGuid():N}",
            world => manager = new FakeWorldManager(world)
            {
                FlushFailure = saveFailure,
                DisposeFailure = new IOException("The disk is still full.")
            });

        var thrown = await Assert.ThrowsAsync<IOException>(() => context.Server.StopAsync());

        // Each step ran although the ones before it failed, and the stop reports the first failure: the save's.
        Assert.True(manager!.Disposed);
        Assert.True(context.UserCache.Saved);
        Assert.Same(saveFailure, thrown);
    }
}
