using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.API.World;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.Utilities;
using Obsidian.WorldData;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Biomes a plugin fills through <see cref="ILevel.FillBiomes"/> reach the players who have the chunks, and only them.
/// The packet's bytes and vanilla's grouping are checked against a capture in <see cref="VanillaBlockChangePackets"/>.
/// </summary>
public sealed class BiomeResends
{
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PluginFillReachesOnlyPlayersWithTheChunks()
    {
        await using var context = TestServerFactory.CreateServer($"biome-resends-{Guid.NewGuid():N}");
        var world = context.World;
        var west = WorldBlockEntities.AddEmptyChunk(world, 0, 0);
        var east = WorldBlockEntities.AddEmptyChunk(world, 1, 0);

        // One player has both chunks, one only the east one, and one neither.
        var both = Join(context, "Both", (0, 0), (1, 0));
        var eastOnly = Join(context, "EastOnly", (1, 0));
        var neither = Join(context, "Neither");
        Assert.True(await context.Server.PauseAsync());
        ClearPackets(both, eastOnly, neither);

        // A plugin fills from its own thread, across both chunks; the next flush sends it.
        ILevel level = world;
        var desert = CodecRegistry.Biomes.All["minecraft:desert"];
        var changed = await Task.Run(() => level.FillBiomes(new Vector(8, 64, 0), new Vector(23, 64, 3), desert));
        await context.Server.RunTickTurnAsync();

        // The quarts at x 8, 12, 16 and 20: two in each chunk.
        Assert.Equal(4, changed);
        Assert.Equal(desert.Id, west.GetBiome(8, 64, 0).Id);
        Assert.Equal(desert.Id, east.GetBiome(20, 64, 0).Id);

        var westBiomes = ChunkBiome.Read(west).Data.ToArray();
        var eastBiomes = ChunkBiome.Read(east).Data.ToArray();
        var toBoth = Assert.Single(BiomePackets(both));
        Assert.Equal([(0, 0), (1, 0)], toBoth.ChunkBiomes.Select(chunk => (chunk.X, chunk.Z)));
        Assert.Equal(westBiomes, toBoth.ChunkBiomes[0].Data.ToArray());
        Assert.Equal(eastBiomes, toBoth.ChunkBiomes[1].Data.ToArray());

        var toEastOnly = Assert.Single(BiomePackets(eastOnly));
        var listed = Assert.Single(toEastOnly.ChunkBiomes);
        Assert.Equal((1, 0), (listed.X, listed.Z));
        Assert.Equal(eastBiomes, listed.Data.ToArray());

        Assert.Empty(BiomePackets(neither));
    }

    private static Entities.Player Join(RealServerContext context, string name, params (int X, int Z)[] chunks)
    {
        var position = new VectorD(8, 70, 8);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, name, position);
        Assert.True(context.World.TryAddPlayer(player));
        foreach (var (x, z) in chunks)
            player.LoadedChunks.Add(NumericsHelper.IntsToLong(x, z));

        return player;
    }

    private static void ClearPackets(params Entities.Player[] players)
    {
        foreach (var player in players)
            ((TestClient)player.Client).ClearPackets();
    }

    private static ChunksBiomesPacket[] BiomePackets(Entities.Player player) =>
        [.. ((TestClient)player.Client).SentPackets.OfType<ChunksBiomesPacket>()];
}
