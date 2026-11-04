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
    [Fact(DisplayName = "A failed final save still disposes the worlds and fails the stop", Timeout = TestServerFactory.TestTimeout)]
    public async Task FailedFinalSaveStillDisposesTheWorlds()
    {
        var failure = new IOException("The disk is full.");
        FakeWorldManager? manager = null;
        await using var context = TestServerFactory.CreateServer(
            $"shutdown-{Guid.NewGuid():N}",
            world => manager = new FakeWorldManager(world) { FlushFailure = failure });

        var thrown = await Assert.ThrowsAsync<IOException>(() => context.Server.StopAsync());

        // The worlds were disposed, which saves what they can and releases the session lock, and the stop reports
        // the save's own failure.
        Assert.Same(failure, thrown);
        Assert.True(manager!.Disposed);
    }
}
