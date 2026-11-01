using Obsidian.API;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Tests.Fakes;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// A player's view asks the level for its chunks like vanilla's player tickets: generating them doesn't wait for the
/// client to take them, and stops for chunks no view has anymore.
/// </summary>
public sealed class PlayerChunkRequests
{
    // Longer than other tests', like ServerPlayerKeepsTheChunksAroundItLoaded's: the 130 chunks generated here have
    // their light reconciled across their borders when the world is disposed and first saves them.
    [Fact(Timeout = 2 * TestServerFactory.TestTimeout)]
    public async Task ViewIsGeneratedAheadOfTheClientAndDroppedOutOfView()
    {
        await using var context = TestServerFactory.CreateServer($"requests-{Guid.NewGuid():N}");
        var world = context.World;
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Viewer", new VectorD(0.5, 64, 0.5));
        player.ClientInformation = player.ClientInformation with { ViewDistance = 3 };
        Assert.True(world.TryAddPlayer(player));
        var client = (TestClient)player.Client;

        // The client never says it took a batch, so after the first only one batch goes out.
        await player.UpdateChunksAsync();
        var inView = Enumerable.Range(-5, 11).SelectMany(x => Enumerable.Range(-5, 11).Select(z => (x, z)))
            .Where(chunk => player.WantsChunk(chunk.x, chunk.z))
            .ToList();
        await GenerateUntilAsync(world, () => inView.All(chunk => world.GetLoadedChunk(chunk.x, chunk.z) is not null));
        await player.SendPendingChunksAsync();

        Assert.All(inView, chunk => Assert.NotNull(world.GetLoadedChunk(chunk.x, chunk.z)));
        Assert.True(client.SentPackets.OfType<LevelChunkWithLightPacket>().Count() < inView.Count);

        // Far away, the old view's requests are dropped, not generated. The level doesn't get to them before the view
        // moves on: a job of the first view, its chunk already published, would otherwise start the next nearest ones
        // as it finishes.
        world.ChunkManagementPaused = true;
        player.Position = new VectorD(10_000.5, 64, 0.5);
        await player.UpdateChunksAsync();
        Assert.True(world.ChunksToGenCount > 0);

        player.Position = new VectorD(20_000.5, 64, 0.5);
        player.ClientInformation = player.ClientInformation with { ViewDistance = 2 };
        await player.UpdateChunksAsync();
        world.ChunkManagementPaused = false;

        await GenerateUntilAsync(world, () => world.ChunksToGenCount == 0 && world.GetLoadedChunk(20_000 >> 4, 0) is not null);
        Assert.Null(world.GetLoadedChunk(10_000 >> 4, 0));
    }

    // Starts queued jobs until the condition holds. How long generation takes depends on how busy the machine is, so
    // this fails only once it stops making progress: no chunk loaded and no request dropped for a while.
    private static async Task GenerateUntilAsync(WorldData.World world, Func<bool> done)
    {
        var progress = (world.LoadedChunks.Count, world.ChunksToGenCount);
        var stalled = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(stalled.Elapsed < TimeSpan.FromSeconds(20), "Generation stopped making progress.");
            await world.ManageChunksAsync();
            await Task.Delay(10);

            var now = (world.LoadedChunks.Count, world.ChunksToGenCount);
            if (now != progress)
            {
                progress = now;
                stalled.Restart();
            }
        }
    }
}
