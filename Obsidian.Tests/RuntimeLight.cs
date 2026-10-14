using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Registries;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Registries.Codecs;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Lighting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Obsidian.Tests;

[Collection(nameof(PluginTagFiles))]
public sealed class RuntimeLightTests(ITestOutputHelper output)
{
    [Fact]
    public void PlaceAndRemoveSourcesAcrossChunkAndSectionBorders()
    {
        var chunks = CreateArea(false);
        var engine = Track(chunks, false);
        var position = new Vector(15, 15, 8);
        chunks[(0, 0)].SetBlock(position.X, position.Y, position.Z, BlocksRegistry.Glowstone);
        Assert.Equal(0, Light(chunks, position, LightType.Block));
        engine.Flush();
        Assert.Equal(15, Light(chunks, position, LightType.Block));
        Assert.Equal(14, Light(chunks, position + Vector.East, LightType.Block));
        Assert.Equal(14, Light(chunks, position + Vector.Up, LightType.Block));
        Assert.Equal(0, Light(chunks, position, LightType.Sky));
        AssertMatchesFull(chunks, false);

        // An independent, weaker source survives the decrease pass of the stronger source.
        var other = new Vector(18, 15, 8);
        chunks[(1, 0)].SetBlock(other.X, other.Y, other.Z, BlocksRegistry.Torch);
        engine.Flush();
        chunks[(0, 0)].SetBlock(position.X, position.Y, position.Z, BlocksRegistry.Air);
        engine.Flush();
        Assert.Equal(11, Light(chunks, position, LightType.Block));
        AssertMatchesFull(chunks, false);
        chunks[(1, 0)].SetBlock(other.X, other.Y, other.Z, BlocksRegistry.Air);
        engine.Flush();
        Assert.All(chunks.Values.SelectMany(chunk => chunk.Sections.ToArray()),
            section => Assert.All(section.BlockLightArray.ToArray(), value => Assert.Equal(0, value)));
    }

    [Fact]
    public void SkyObstructionAndDirectionalShapesMatchFullComputation()
    {
        var chunks = CreateArea(true);
        var engine = Track(chunks, true);
        var position = new Vector(15, 20, 8);
        chunks[(0, 0)].SetBlock(position.X, position.Y, position.Z, BlocksRegistry.Stone);
        engine.Flush();
        Assert.Equal(0, Light(chunks, position, LightType.Sky));
        Assert.Equal(14, Light(chunks, position + Vector.Down, LightType.Sky));
        Assert.Equal(15, Light(chunks, position + Vector.East, LightType.Sky));
        AssertMatchesFull(chunks, true);

        var slab = BlocksRegistry.GetDefault(BlockIds.OakSlab);
        foreach (var block in new[] { slab, BlocksRegistry.Water, BlocksRegistry.Air })
        {
            chunks[(0, 0)].SetBlock(position.X, position.Y, position.Z, block);
            engine.Flush();
            AssertMatchesFull(chunks, true);
        }

        Assert.Equal(15, Light(chunks, position + Vector.Down, LightType.Sky));
        // The virtual sky source above the world must still illuminate a water block at the top.
        var top = new Vector(8, 31, 8);
        chunks[(0, 0)].SetBlock(top.X, top.Y, top.Z, BlocksRegistry.Water);
        engine.Flush();
        Assert.Equal(14, Light(chunks, top, LightType.Sky));
        AssertMatchesFull(chunks, true);
    }

    [Fact]
    public void LoadedNeighborRepairsStaleLightAfterAnUnloadedSourceChanges()
    {
        var chunks = CreateArea(false);
        var engine = Track(chunks, false);
        var position = new Vector(-1, 0, 8);
        chunks[(-1, 0)].SetBlock(position.X, position.Y, position.Z, BlocksRegistry.Glowstone);
        engine.Flush();
        var unloaded = chunks[(0, 0)];
        engine.Untrack(unloaded);
        chunks[(-1, 0)].SetBlock(position.X, position.Y, position.Z, BlocksRegistry.Air);
        engine.Flush();
        Assert.Equal(14, unloaded.GetLightLevel(0, 0, 8, LightType.Block));

        engine.Track(unloaded);
        engine.Flush();
        Assert.Equal(0, unloaded.GetLightLevel(0, 0, 8, LightType.Block));
        AssertMatchesFull(chunks, false);

        engine.Untrack(unloaded);
        chunks[(-1, 0)].SetBlock(position.X, position.Y, position.Z, BlocksRegistry.Glowstone);
        engine.Flush();
        engine.Track(unloaded);
        engine.Flush();
        Assert.Equal(14, unloaded.GetLightLevel(0, 0, 8, LightType.Block));
        AssertMatchesFull(chunks, false);
    }

    [Fact]
    public void BatchedEditsMatchFullComputationAndReportTiming()
    {
        var chunks = CreateArea(true);
        var engine = Track(chunks, true);
        var random = new Random(712);
        IBlock[] blocks =
        [
            BlocksRegistry.Stone,
            BlocksRegistry.Glowstone,
            BlocksRegistry.Water,
            BlocksRegistry.GetDefault(BlockIds.OakSlab),
            BlocksRegistry.GetDefault(BlockIds.OakLeaves),
            BlocksRegistry.Air
        ];
        var incremental = TimeSpan.Zero;
        var full = TimeSpan.Zero;
        for (var batch = 0; batch < 12; batch++)
        {
            var timer = Stopwatch.StartNew();
            for (var edit = 0; edit < 24; edit++)
            {
                var position = new Vector(random.Next(-4, 20), random.Next(-20, 32), random.Next(-4, 20));
                var chunk = chunks[(position.X >> 4, position.Z >> 4)];
                chunk.SetBlock(position.X, position.Y, position.Z, blocks[random.Next(blocks.Length)]);
            }

            engine.Flush();
            incremental += timer.Elapsed;
            timer.Restart();
            AssertMatchesFull(chunks, true);
            full += timer.Elapsed;
        }

        output.WriteLine($"288 edits in 12 batches: incremental {incremental.TotalMilliseconds:F1} ms; "
            + $"full recomputation and comparison {full.TotalMilliseconds:F1} ms (9 chunks, 64 layers).");
    }

    [Fact]
    public void PluginEmissionUsesTheSameRuntimePropagation()
    {
        var properties = new BlockProperties { LightEmission = 7 };
        var type = BlocksRegistry.Register("runtime_light:lamp", properties);
        var plugin = new LightPlugin();
        Assert.True(BlocksRegistry.TryBind(type, properties, plugin, () => false));
        var chunks = CreateArea(false);
        var engine = Track(chunks, false);
        var position = new Vector(8, 0, 8);
        chunks[(0, 0)].SetBlock(position.X, position.Y, position.Z, type.DefaultState);
        engine.Flush();
        Assert.Equal(7, Light(chunks, position, LightType.Block));
        Assert.Equal(6, Light(chunks, position + Vector.East, LightType.Block));
        AssertMatchesFull(chunks, false);
        chunks[(0, 0)].SetBlock(position.X, position.Y, position.Z, BlocksRegistry.Air);
        engine.Flush();
        Assert.Equal(0, Light(chunks, position + Vector.East, LightType.Block));
        BlocksRegistry.ReleasePlugin(plugin);
    }

    [Fact]
    public void PacketsBatchChangedSectionsSnapshotDataAndClearDarkSections()
    {
        var chunks = CreateArea(false);
        var engine = Track(chunks, false);
        var chunk = chunks[(0, 0)];
        var position = new Vector(8, -24, 8);
        chunk.SetBlock(position.X, position.Y, position.Z, BlocksRegistry.Glowstone);
        chunk.SetBlock(position.X + 1, position.Y, position.Z, BlocksRegistry.Torch);
        var packets = engine.TakeUpdates();
        Assert.Single(packets, packet => ReferenceEquals(packet.Chunk, chunk));
        var packet = packets.Single(packet => ReferenceEquals(packet.Chunk, chunk));
        var bytes = Serialize(packet);

        chunk.SetBlock(position.X, position.Y, position.Z, BlocksRegistry.Air);
        chunk.SetBlock(position.X + 1, position.Y, position.Z, BlocksRegistry.Air);
        engine.Flush();
        Assert.Equal(bytes, Serialize(packet));
        var dark = engine.TakeUpdates().Single(update => ReferenceEquals(update.Chunk, chunk));
        using var reader = new NetworkBuffer(Serialize(dark));
        Assert.Equal(0, reader.ReadVarInt());
        Assert.Equal(0, reader.ReadVarInt());
        Assert.Empty(reader.ReadLongArray()); // Sky data mask.
        Assert.Empty(reader.ReadLongArray()); // Block data mask.
        Assert.Empty(reader.ReadLongArray()); // Sky empty mask: sky did not change.
        Assert.Equal([6L], reader.ReadLongArray()); // Only sections 0 and 1 became dark (bits 1 and 2).
        Assert.Equal(0, reader.ReadVarInt());
        Assert.Equal(0, reader.ReadVarInt());
        Assert.Empty(engine.TakeUpdates());

        chunk.SetBlock(position.X, position.Y, position.Z, BlocksRegistry.Glowstone);
        chunk.SetBlock(position.X, position.Y, position.Z, BlocksRegistry.Air);
        Assert.Empty(engine.TakeUpdates());
    }

    [Fact]
    public async Task TickSendsLightOnlyToWatchersAndSaveFlushesPendingChanges()
    {
        await using var context = TestServerFactory.CreateServer($"runtime-light-{Guid.NewGuid():N}");
        var world = context.World;
        var chunk = new Chunk(0, 0, world.MinY, world.Height, ChunkGenStage.full);
        for (var y = world.MinY; y < world.MinY + world.Height; y += 16)
            chunk.SetBiome(0, y, 0, CodecRegistry.Biomes.Plains);

        LightEngine.LightChunk(chunk, [], true);
        var region = (Region)world.LoadRegionByChunk(0, 0);
        region.SetChunk(chunk);
        var watcher = TestServerFactory.CreateConnectedPlayer(context.Server, world, "LightWatcher");
        var outsider = TestServerFactory.CreateConnectedPlayer(context.Server, world, "LightOutsider");
        watcher.LoadedChunks.Add(0L);
        world.Players.TryAdd(watcher.Uuid, watcher);
        world.Players.TryAdd(outsider.Uuid, outsider);
        var watcherClient = (TestClient)watcher.Client;
        var outsiderClient = (TestClient)outsider.Client;
        try
        {
            await world.SetBlockUntrackedAsync(8, 70, 8, BlocksRegistry.Glowstone);
            lock (world.Fluids.TickLock)
            {
                Assert.True(world.Fluids.SetBlock(new Vector(9, 70, 8), BlocksRegistry.Torch));
                var feature = new LiveFeatureLevel(world);
                Assert.True(feature.SetBlock(new Vector(10, 70, 8), BlocksRegistry.Torch));
                feature.Commit();
            }

            Assert.Empty(watcherClient.SentPackets.OfType<LightUpdatePacket>());
            await world.DoWorldTickAsync();
            Assert.Single(watcherClient.SentPackets.OfType<LightUpdatePacket>());
            Assert.Empty(outsiderClient.SentPackets.OfType<LightUpdatePacket>());

            // A save before the next tick must persist the pending decrease and still leave a packet for that tick.
            watcherClient.ClearPackets();
            await world.SetBlockUntrackedAsync(8, 70, 8, BlocksRegistry.Air);
            await world.SetBlockUntrackedAsync(9, 70, 8, BlocksRegistry.Air);
            await world.SetBlockUntrackedAsync(10, 70, 8, BlocksRegistry.Air);
            await region.FlushAsync();
            Assert.Equal(0, chunk.GetLightLevel(8, 70, 8, LightType.Block));
            Assert.Empty(watcherClient.SentPackets.OfType<LightUpdatePacket>());
            await region.UnloadChunk(0, 0);
            var loaded = await world.GetChunkAsync(0, 0, scheduleGeneration: false);
            Assert.NotSame(chunk, loaded);
            Assert.Equal(0, loaded.GetLightLevel(8, 70, 8, LightType.Block));
            Assert.Equal(15, loaded.GetLightLevel(8, 70, 8, LightType.Sky));

            await world.DoWorldTickAsync();
            var update = Assert.Single(watcherClient.SentPackets.OfType<LightUpdatePacket>());
            Assert.Same(loaded, update.Chunk);
            Assert.Empty(outsiderClient.SentPackets.OfType<LightUpdatePacket>());
        }
        finally
        {
            world.Players.TryRemove(watcher.Uuid, out _);
            world.Players.TryRemove(outsider.Uuid, out _);
        }
    }

    [Fact]
    public async Task DirectChunkWritesShareTheLevelTickLock()
    {
        var chunks = CreateArea(false);
        var engine = Track(chunks, false);
        using var started = new ManualResetEventSlim();
        Task write;
        lock (engine.TickLock)
        {
            write = Task.Run(() =>
            {
                started.Set();
                chunks[(0, 0)].SetBlock(8, 0, 8, BlocksRegistry.Glowstone);
            });
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(write.IsCompleted);
        }

        await write;
        engine.Flush();
        Assert.Equal(14, Light(chunks, new Vector(9, 0, 8), LightType.Block));
    }

    private static Dictionary<(int X, int Z), Chunk> CreateArea(bool sky)
    {
        var chunks = new Dictionary<(int X, int Z), Chunk>();
        for (var z = -1; z <= 1; z++)
        {
            for (var x = -1; x <= 1; x++)
            {
                var chunk = new Chunk(x, z, -32, 64, ChunkGenStage.full);
                for (var y = -32; y < 32; y += 16)
                    chunk.SetBiome(0, y, 0, CodecRegistry.Biomes.Plains);

                chunks[(x, z)] = chunk;
            }
        }

        Compute(chunks.Values.ToArray(), sky);
        return chunks;
    }

    private static RuntimeLightEngine Track(Dictionary<(int X, int Z), Chunk> chunks, bool sky)
    {
        var engine = new RuntimeLightEngine(new Lock(), () => sky);
        foreach (var chunk in chunks.Values)
            engine.Track(chunk);

        Assert.Empty(engine.TakeUpdates());
        return engine;
    }

    private static int Light(Dictionary<(int X, int Z), Chunk> chunks, Vector position, LightType type) =>
        chunks[(position.X >> 4, position.Z >> 4)].GetLightLevel(position.X, position.Y, position.Z, type);

    private static void AssertMatchesFull(Dictionary<(int X, int Z), Chunk> chunks, bool sky)
    {
        var copies = chunks.Values.Select(chunk => chunk.Clone(chunk.X, chunk.Z)).ToArray();
        foreach (var chunk in copies)
        {
            foreach (var section in chunk.Sections)
            {
                section.SetLight(new byte[2048], LightType.Block);
                section.SetLight(new byte[2048], LightType.Sky);
            }
        }

        Compute(copies, sky);
        foreach (var copy in copies)
        {
            var actual = chunks[(copy.X, copy.Z)];
            for (var index = 0; index < copy.Sections.Length; index++)
            {
                Assert.Equal(copy.Sections[index].BlockLightArray.ToArray(),
                    actual.Sections[index].BlockLightArray.ToArray());
                Assert.Equal(copy.Sections[index].SkyLightArray.ToArray(),
                    actual.Sections[index].SkyLightArray.ToArray());
            }
        }
    }

    private static void Compute(IChunk[] chunks, bool sky)
    {
        var lit = new List<IChunk>();
        foreach (var chunk in chunks)
        {
            LightEngine.LightChunk(chunk,
                lit.Where(other => Math.Abs(other.X - chunk.X) <= 1 && Math.Abs(other.Z - chunk.Z) <= 1), sky);
            lit.Add(chunk);
        }
    }

    private static byte[] Serialize(LightUpdatePacket packet)
    {
        using var buffer = new NetworkBuffer();
        packet.Serialize(buffer);
        return buffer.GetBuffer().AsSpan(0, buffer.Size).ToArray();
    }

    private sealed class LightPlugin : Obsidian.API.Plugins.IPluginContainer
    {
        public Obsidian.API.Plugins.PluginBase Plugin => null!;
        public Microsoft.Extensions.DependencyInjection.IServiceScope ServiceScope => null!;
        public Assembly PluginAssembly => typeof(RuntimeLightTests).Assembly;

        public void InjectServices(Microsoft.Extensions.Logging.ILogger logger, object module) { }

        public byte[] GetFileData(string fileName) => null!;
    }
}
