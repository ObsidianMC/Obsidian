using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.ChunkData;
using Obsidian.Nbt;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using Obsidian.WorldData.Lighting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class RuntimeHeightmaps
{
    private static readonly JsonElement reference = LoadReference();
    private static readonly bool[] modes = [false, true];

    [Fact]
    public void ReplayVanillaRuntimeHeightmapReference()
    {
        Assert.Equal(ServerConstants.ProtocolDescription, reference.GetProperty("version").GetString());
        FixtureCases.AssertAll(reference.GetProperty("predicates").EnumerateObject(), entry => entry.Name, entry =>
        {
            var map = new Heightmap(Type(entry.Name), new Chunk(0, 0));
            var expected = entry.Value.GetString()!;
            Assert.Equal(BlocksRegistry.StateToNumeric.Length, expected.Length);
            for (var id = 0; id < expected.Length; id++)
            {
                var state = BlocksRegistry.Get(id);
                Assert.True(map.Predicate(state) == (expected[id] == '1'), $"{entry.Name}, state {id}: {state}");
            }
        });

        FixtureCases.AssertAll(reference.GetProperty("cases").EnumerateArray(), Name, input =>
        {
            foreach (var liveSections in modes)
            {
                var chunk = Create(input);
                if (liveSections)
                    new RuntimeLightEngine(new Lock(), () => false).Track(chunk);

                AssertMaps(chunk, input.GetProperty("initial"), 0, 0);
                var index = 0;
                foreach (var step in input.GetProperty("steps").EnumerateArray())
                {
                    var pos = Position(step);
                    if (liveSections)
                    {
                        var section = (ChunkSection)chunk.Sections[(pos.Y - chunk.MinY) >> 4];
                        var state = VanillaSulfurBlocks.ParseState(step.GetProperty("state").GetString()!);
                        if (index++ % 2 == 0)
                            section.SetBlock(pos.X & 15, pos.Y & 15, pos.Z & 15, state);
                        else
                            section.SetStateId(pos.X & 15, pos.Y & 15, pos.Z & 15, state.GetHashCode());
                    }
                    else
                    {
                        Apply(chunk, step);
                    }

                    var expected = step.GetProperty("heightmaps");
                    AssertMaps(chunk, expected, pos.X, pos.Z);
                    AssertPacket(chunk, expected);
                }
            }
        });
    }

    [Fact]
    public async Task SavedRuntimeMapsRoundTripAndMissingMapsAreRebuilt()
    {
        var input = PackedColumns();
        var steps = input.GetProperty("steps").EnumerateArray().ToArray();
        foreach (var missingMaps in modes)
        {
            var folder = Path.Join(Path.GetTempPath(), $"obsidian-heights-{Guid.NewGuid():N}");
            try
            {
                await using (var region = new Region(-1, 0, folder, "region"))
                {
                    await region.InitAsync();
                    var chunk = Create(input);
                    foreach (var step in steps.Take(5))
                        Apply(chunk, step);

                    if (missingMaps)
                    {
                        chunk.Heightmaps.Remove(HeightmapType.OceanFloor);
                        chunk.Heightmaps.Remove(HeightmapType.MotionBlockingNoLeaves);
                    }

                    region.SetChunk(chunk);
                    await region.FlushAsync();
                }

                if (!missingMaps)
                {
                    await using var file = new RegionFile(Path.Join(folder, "region", "r.-1.0.mca"),
                        NbtCompression.ZLib);
                    await file.InitializeAsync();
                    var bytes = (await file.GetChunkBytesAsync(29, 2))!.Value;
                    using var stream = new MemoryStream(bytes.ToArray());
                    var saved = Assert.IsType<NbtCompound>(new NbtReader(stream).ReadNextTag());
                    var maps = Assert.IsType<NbtCompound>(saved["Heightmaps"]);
                    foreach (var map in steps[4].GetProperty("heightmaps").EnumerateObject())
                        Assert.Equal(Words(map.Value), ((NbtArray<long>)maps[map.Name]).GetArray());
                }

                await using (var region = new Region(-1, 0, folder, "region"))
                {
                    await region.InitAsync();
                    var chunk = Assert.IsType<Chunk>(await region.GetChunkAsync(-3, 2));
                    AssertMaps(chunk, steps[4].GetProperty("heightmaps"), 15, 15);
                    AssertPacket(chunk, steps[4].GetProperty("heightmaps"));
                    Apply(chunk, steps[5]);
                    AssertMaps(chunk, steps[5].GetProperty("heightmaps"), 0, 0);
                    await region.FlushAsync();
                }

                await using (var region = new Region(-1, 0, folder, "region"))
                {
                    await region.InitAsync();
                    var chunk = Assert.IsType<Chunk>(await region.GetChunkAsync(-3, 2));
                    AssertMaps(chunk, steps[5].GetProperty("heightmaps"), 0, 0);
                    Assert.Equal(BlocksRegistry.Air, chunk.GetBlock(0, 319, 0));
                }
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ConcurrentColumnWritesPreservePackedEntriesAndSnapshots()
    {
        var input = PackedColumns();
        var steps = input.GetProperty("steps").EnumerateArray().ToArray();
        var chunk = Create(input);
        var columns = steps.GroupBy(step => (Position(step).X, Position(step).Z)).ToArray();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writers = columns.Concat(columns.Take(1)).Select(async column =>
        {
            await start.Task;
            for (var repeat = 0; repeat < 100; repeat++)
            {
                foreach (var step in column)
                    Apply(chunk, step);
            }

            Apply(chunk, column.First());
        }).ToArray();
        var snapshots = Task.Run(async () =>
        {
            await start.Task;
            for (var repeat = 0; repeat < 100; repeat++)
            {
                var copy = Assert.IsType<Chunk>(chunk.Clone(-3, 2));
                foreach (var column in columns)
                {
                    var placement = column.First();
                    var pos = Position(placement);
                    var expected = placement.GetProperty("heightmaps");
                    var present = !copy.GetBlock(pos.X, pos.Y, pos.Z).IsAir;
                    foreach (var map in expected.EnumerateObject())
                    {
                        var checkpoint = present ? expected : input.GetProperty("initial");
                        var height = checkpoint.GetProperty(map.Name).GetProperty("height").GetInt32();
                        Assert.Equal(height, copy.Heightmaps[Type(map.Name)].GetHeight(pos.X, pos.Z));
                    }
                }
            }
        });

        start.SetResult();
        await Task.WhenAll(writers.Append(snapshots));
        AssertMaps(chunk, steps[4].GetProperty("heightmaps"), 15, 15);
        AssertPacket(chunk, steps[4].GetProperty("heightmaps"));
    }

    private static Chunk Create(JsonElement input)
    {
        var chunk = new Chunk(-3, 2, input.GetProperty("minY").GetInt32(), input.GetProperty("height").GetInt32());
        chunk.SetChunkStatus(ChunkGenStage.full);
        foreach (var section in chunk.Sections)
            section.SetBiome(0, 0, 0, CodecRegistry.Biomes.Plains);

        return chunk;
    }

    private static void Apply(Chunk chunk, JsonElement step)
    {
        var pos = Position(step);
        chunk.SetBlock(pos.X, pos.Y, pos.Z, VanillaSulfurBlocks.ParseState(step.GetProperty("state").GetString()!));
    }

    private static void AssertMaps(Chunk chunk, JsonElement expected, int x, int z)
    {
        Assert.Equal(4, chunk.Heightmaps.Count);
        foreach (var map in expected.EnumerateObject())
        {
            var actual = chunk.Heightmaps[Type(map.Name)];
            Assert.Equal(map.Value.GetProperty("height").GetInt32(), actual.GetHeight(x, z));
            Assert.Equal(Words(map.Value), actual.GetDataArray());
        }
    }

    private static void AssertPacket(Chunk chunk, JsonElement expected)
    {
        using var data = new NetworkBuffer();
        new LevelChunkWithLightPacket(chunk).Serialize(data);
        using var reader = new NetworkBuffer(data.AsSpan(0, data.Size).ToArray());
        Assert.Equal(chunk.X, reader.ReadInt());
        Assert.Equal(chunk.Z, reader.ReadInt());
        Assert.Equal(3, reader.ReadVarInt());
        var maps = new Dictionary<HeightmapType, long[]>();
        for (var index = 0; index < 3; index++)
        {
            var type = (HeightmapType)reader.ReadVarInt();
            var words = new long[reader.ReadVarInt()];
            for (var word = 0; word < words.Length; word++)
                words[word] = reader.ReadLong();

            maps.Add(type, words);
        }

        Assert.DoesNotContain(HeightmapType.OceanFloor, maps.Keys);
        foreach (var map in expected.EnumerateObject().Where(map => Type(map.Name) != HeightmapType.OceanFloor))
            Assert.Equal(Words(map.Value), maps[Type(map.Name)]);
    }

    private static string Name(JsonElement input) => input.GetProperty("name").GetString()!;

    private static Vector Position(JsonElement step)
    {
        var pos = step.GetProperty("pos");
        return new Vector(pos[0].GetInt32(), pos[1].GetInt32(), pos[2].GetInt32());
    }

    private static HeightmapType Type(string name) =>
        Enum.Parse<HeightmapType>(name.Replace("_", ""), ignoreCase: true);

    private static long[] Words(JsonElement map) =>
        map.GetProperty("packed").EnumerateArray().Select(value => value.GetInt64()).ToArray();

    private static JsonElement PackedColumns() =>
        reference.GetProperty("cases").EnumerateArray()
            .Single(input => Name(input) == "packed_columns_and_build_height");

    private static JsonElement LoadReference()
    {
        using var stream = typeof(RuntimeHeightmaps).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.heightmaps.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }
}
