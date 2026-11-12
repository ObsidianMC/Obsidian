using Obsidian.API;
using Obsidian.API.Commands;
using Obsidian.API.Registries;
using Obsidian.API.World;
using Obsidian.Commands;
using Obsidian.Commands.Framework;
using Obsidian.Commands.Modules;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.Utilities;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Biomes a plugin fills through <see cref="ILevel.FillBiomes"/> reach the players who have the chunks, and only them,
/// and fills leave alone a chunk a region unload has already snapshotted.
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

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task FillsLeaveChunksBeingUnloadedAlone()
    {
        await using var context = TestServerFactory.CreateServer($"biome-resends-unload-{Guid.NewGuid():N}");
        ((CommandHandler)context.Server.CommandHandler).RegisterCommandClass<WorldCommandModule>(null);
        var world = context.World;

        // Chunk 31 is the last of region 0 and chunk 32 the first of region 1. Region 0's unload has taken its final
        // snapshot, so its chunk is still in its slot but no longer resident: a change to it would be lost.
        var unloading = WorldBlockEntities.AddEmptyChunk(world, 31, 0);
        var live = WorldBlockEntities.AddEmptyChunk(world, 32, 0);
        var player = Join(context, "Both", (31, 0), (32, 0));
        Assert.True(await context.Server.PauseAsync());
        var region = (Region)world.GetRegionForChunk(31, 0)!;
        region.Retire();
        ClearPackets(player);

        // /fillbiome over both chunks fails as unloaded, changing neither.
        var desert = CodecRegistry.Biomes.All["minecraft:desert"];
        var feedback = await FillBiomeCommandAsync(context, "fillbiome 508 64 0 515 64 3 minecraft:desert");
        await context.Server.RunTickTurnAsync();

        Assert.Equal(["That position is not loaded"], feedback);
        Assert.NotEqual(desert.Id, unloading.GetBiome(508, 64, 0).Id);
        Assert.NotEqual(desert.Id, live.GetBiome(512, 64, 0).Id);
        Assert.Empty(BiomePackets(player));

        // The level method fills only the live chunk, which the player is sent.
        ILevel level = world;
        var changed = level.FillBiomes(new Vector(508, 64, 0), new Vector(515, 64, 3), desert);
        await context.Server.RunTickTurnAsync();

        // The box's quarts are at x 508 (chunk 31) and 512 (chunk 32).
        Assert.Equal(1, changed);
        Assert.NotEqual(desert.Id, unloading.GetBiome(508, 64, 0).Id);
        Assert.Equal(desert.Id, live.GetBiome(512, 64, 0).Id);
        var sent = Assert.Single(BiomePackets(player));
        var listed = Assert.Single(sent.ChunkBiomes);
        Assert.Equal((32, 0), (listed.X, listed.Z));

        region.Unretire();
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task FilterIsAskedOnceForEachQuart()
    {
        await using var context = TestServerFactory.CreateServer($"biome-resends-filter-{Guid.NewGuid():N}");
        var chunk = WorldBlockEntities.AddEmptyChunk(context.World, 0, 0);
        var before = chunk.GetBiome(0, 64, 0);

        // A filter that accepts only the first quart it's shown: the fill takes that quart and no other, so asking
        // it again before writing would change nothing and return 0.
        ILevel level = context.World;
        var desert = CodecRegistry.Biomes.All["minecraft:desert"];
        Assert.NotEqual(desert.Id, before.Id);
        var calls = 0;
        var changed = level.FillBiomes(new Vector(0, 64, 0), new Vector(7, 64, 7), desert, _ => ++calls == 1);

        // The box holds the quarts at x 0 and 4 and z 0 and 4, visited x, then z, from (0, 64, 0).
        Assert.Equal(4, calls);
        Assert.Equal(1, changed);
        Assert.Equal(desert.Id, chunk.GetBiome(0, 64, 0).Id);
        Assert.Equal(before.Id, chunk.GetBiome(0, 64, 4).Id);
        Assert.Equal(before.Id, chunk.GetBiome(4, 64, 0).Id);
        Assert.Equal(before.Id, chunk.GetBiome(4, 64, 4).Id);
    }

    // Runs a command from the console in the test's world, returning what it said back.
    private static async Task<List<string>> FillBiomeCommandAsync(RealServerContext context, string text)
    {
        var feedback = new Feedback();
        var command = new CommandContext("/" + text, feedback, null, context.Server) { Level = context.World };
        await ((CommandHandler)context.Server.CommandHandler).ProcessCommand(command);
        return feedback.Messages;
    }

    private sealed class Feedback : ICommandSender
    {
        public CommandIssuers Issuer => CommandIssuers.Console;

        public IPlayer? Player => null;

        public List<string> Messages { get; } = [];

        public Task SendMessageAsync(ChatMessage message)
        {
            this.Messages.Add(StoredCommand.PlainText(message));
            return Task.CompletedTask;
        }

        public Task SendMessageAsync(ChatMessage message, Guid sender) => this.SendMessageAsync(message);
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
