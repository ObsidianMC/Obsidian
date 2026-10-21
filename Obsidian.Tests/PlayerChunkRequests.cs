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
    [Fact(Timeout = TestServerFactory.TestTimeout)]
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

        // Far away, the old view's requests are dropped, not generated.
        player.Position = new VectorD(10_000.5, 64, 0.5);
        await player.UpdateChunksAsync();
        Assert.True(world.ChunksToGenCount > 0);
        player.Position = new VectorD(20_000.5, 64, 0.5);
        player.ClientInformation = player.ClientInformation with { ViewDistance = 2 };
        await player.UpdateChunksAsync();
        await GenerateUntilAsync(world, () => world.ChunksToGenCount == 0 && world.GetLoadedChunk(20_000 >> 4, 0) is not null);
        Assert.Null(world.GetLoadedChunk(10_000 >> 4, 0));
    }

    // Starts queued jobs until the condition holds.
    private static async Task GenerateUntilAsync(WorldData.World world, Func<bool> done)
    {
        var timeout = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(timeout.Elapsed < TimeSpan.FromSeconds(20), "Generation didn't finish.");
            await world.ManageChunksAsync();
            await Task.Delay(10);
        }
    }
}
