using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Configuration;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators;
using Obsidian.WorldData.Structures;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>Reference generation must survive proto-chunk reloads and changes to the structure sets.</summary>
[Trait("Category", TestCategory.Worldgen)]
[Collection(nameof(PluginTagFiles))]
public sealed class StructureReferencePersistence
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ReloadedProtoChunksCompleteReferencesAndKeepUnknownEntries(
        bool unknownEntry, bool retryAfterFailure)
    {
        const int x = -126;
        const int z = -106;
        var generator = new MojangGenerator();
        var world = CreateWorld(generator, $"reference-proto-{Guid.NewGuid():N}");
        try
        {
            var proto = (Chunk)await world.GetChunkAsync(x, z, scheduleGeneration: false);
            Assert.Equal(ChunkGenStage.structure_starts, proto.ChunkStatus);
            if (unknownEntry)
            {
                proto.StructureReferences = new NbtCompound("References")
                {
                    new NbtArray<long>("missing:structure", [123L])
                };
            }

            var region = (Region)world.LoadRegionByChunk(x, z);
            await region.UnloadChunk(x, z);
            var loaded = (Chunk)await world.GetChunkAsync(x, z, scheduleGeneration: false);
            Assert.NotSame(proto, loaded);
            Assert.Equal(ChunkGenStage.structure_starts, loaded.ChunkStatus);
            Assert.NotNull(loaded.StructureReferences);

            if (retryAfterFailure)
            {
                // An unloaded neighbor forces an actual metadata read before the terrain lock is taken.
                _ = await world.GetChunkAsync(x + 1, z, scheduleGeneration: false);
                await region.UnloadChunk(x + 1, z);
                region.ChunkFile.Observe = step => step.Step == RegionFile.Step.Reading
                    ? ValueTask.FromException(new IOException("Injected reference read failure."))
                    : ValueTask.CompletedTask;
                try
                {
                    await Assert.ThrowsAsync<IOException>(async () =>
                        await generator.GenerateChunkAsync(x, z, loaded, ChunkGenStage.carvers));
                    Assert.Equal(ChunkGenStage.structure_starts, loaded.ChunkStatus);
                }
                finally
                {
                    region.ChunkFile.Observe = null;
                }
            }

            var generated = (Chunk)await generator.GenerateChunkAsync(x, z, loaded);
            Assert.Equal(ChunkGenStage.full, generated.ChunkStatus);
            AssertReference(generated, "minecraft:swamp_hut", Packed(x, z));
            if (unknownEntry)
                AssertReference(generated, "missing:structure", 123L);

            await region.UnloadChunk(x, z);
            var saved = (Chunk)await world.GetChunkAsync(x, z, scheduleGeneration: false);
            AssertReference(saved, "minecraft:swamp_hut", Packed(x, z));
            if (unknownEntry)
                AssertReference(saved, "missing:structure", 123L);
        }
        finally
        {
            await DisposeWorld(world);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavedStartsStillReferenceNewNeighborsAfterTheirSetIsRemoved(bool restart)
    {
        const int x = -320;
        const int z = -304;
        const string pyramid = "minecraft:desert_pyramid";
        var source = PluginStructures.CurrentRegistry;
        var generator = new MojangGenerator { Structures = () => source };
        var name = $"reference-set-removal-{Guid.NewGuid():N}";
        var world = CreateWorld(generator, name);
        try
        {
            // Save a real start before its set is removed; the structure itself stays registered.
            _ = await world.GetChunkAsync(x, z, scheduleGeneration: false);
            var manager = generator.Builder!.Structures!;
            var start = manager.GetStarts(x, z).Single(candidate => candidate.Structure.Identifier == pyramid);
            manager.LoadStart(start);
            await world.LoadRegionByChunk(x, z).FlushAsync();
            source = source with { Version = source.Version + 1, Sets = new Dictionary<string, StructureSet>() };
            generator.RefreshStructures();
            Assert.Contains(pyramid, source.Structures.Keys);
            Assert.Empty(manager.GetStarts(x, z));

            if (restart)
            {
                await UnloadRegions(world);
                await world.DisposeAsync();
                PluginDimensions.GiveBack("minecraft:overworld");
                generator = new MojangGenerator { Structures = () => source };
                world = CreateWorld(generator, name);
                Assert.Empty(generator.Builder!.Structures!.GetStarts(x, z));
            }

            var neighbor = (Chunk)await generator.GenerateChunkAsync(x + 1, z, stage: ChunkGenStage.carvers);
            AssertReference(neighbor, pyramid, Packed(x, z));
            await UnloadRegions(world);
            var saved = (Chunk)await world.GetChunkAsync(x + 1, z, scheduleGeneration: false);
            AssertReference(saved, pyramid, Packed(x, z));
        }
        finally
        {
            await DisposeWorld(world);
        }
    }

    [Fact]
    public async Task ReferencesReadUpdatedSavedBoundsAfterChunkUnload()
    {
        const int x = -320;
        const int z = -304;
        const string pyramid = "minecraft:desert_pyramid";
        var source = PluginStructures.CurrentRegistry;
        var generator = new MojangGenerator { Structures = () => source };
        var world = CreateWorld(generator, $"reference-saved-bounds-{Guid.NewGuid():N}");
        try
        {
            var chunk = (Chunk)await world.GetChunkAsync(x, z, scheduleGeneration: false);
            var manager = generator.Builder!.Structures!;
            var start = manager.GetStarts(x, z).Single(candidate => candidate.Structure.Identifier == pyramid);
            chunk.StructureStarts = new NbtCompound("starts") { start.SaveState() };
            var region = (Region)world.LoadRegionByChunk(x, z);
            await region.UnloadChunk(x, z);
            Assert.NotEmpty(await region.ReadSavedStructureStartsAsync(x, z));

            // Model a saved structure with changed piece bounds, then remove the set that originally made it.
            var loaded = (Chunk)await world.GetChunkAsync(x, z, scheduleGeneration: false);
            var savedStart = (NbtCompound)loaded.StructureStarts![pyramid];
            var children = (NbtList)savedStart["Children"];
            var box = (NbtArray<int>)((NbtCompound)children[0])["BB"];
            box[0] += 64;
            box[3] += 64;
            loaded.MarkUnsaved();
            source = source with { Version = source.Version + 1, Sets = new Dictionary<string, StructureSet>() };
            generator.RefreshStructures();
            await region.UnloadChunk(x, z);

            var oldNeighbor = (Chunk)await generator.GenerateChunkAsync(x + 1, z, stage: ChunkGenStage.carvers);
            Assert.False(oldNeighbor.StructureReferences!.HasTag(pyramid));
            var neighbor = (Chunk)await generator.GenerateChunkAsync(x + 4, z, stage: ChunkGenStage.carvers);
            AssertReference(neighbor, pyramid, Packed(x, z));
        }
        finally
        {
            await DisposeWorld(world);
        }
    }

    private static void AssertReference(Chunk chunk, string identifier, long position)
    {
        Assert.NotNull(chunk.StructureReferences);
        Assert.True(chunk.StructureReferences.TryGetTag<NbtArray<long>>(identifier, out var references), identifier);
        Assert.Contains(position, references!.GetArray());
    }

    private static long Packed(int x, int z) => (x & 0xFFFFFFFFL) | (z & 0xFFFFFFFFL) << 32;

    private static World CreateWorld(MojangGenerator generator, string name)
    {
        var configuration = new ServerConfiguration { SpawnChunkRadius = 0, TimeTickSpeedMultiplier = 1 };
        var world = new World(
            NullLogger<World>.Instance,
            new FakeWorldManager(defaultWorld: null!),
            new FakePacketBroadcaster(),
            new TestOptionsMonitor<ServerConfiguration>(configuration),
            new FakeEventDispatcher(),
            generator,
            name,
            "12345");
        world.Initialize(new DimensionCodec
        {
            Name = "minecraft:overworld",
            Id = 0,
            Element = new DimensionElement
            {
                MonsterSpawnBlockLightLimit = 0,
                MonsterSpawnLightLevel = new MonsterSpawnLightLevel { IntValue = 0 },
                HasSkylight = true,
                MinY = -64,
                Height = 384,
                LogicalHeight = 384,
                CoordinateScale = 1
            }
        });
        return world;
    }

    private static async Task UnloadRegions(World world)
    {
        foreach (var region in world.Regions.Values.ToArray())
            await world.UnloadRegionAsync(region.X, region.Z);
    }

    private static async Task DisposeWorld(World world)
    {
        await world.DisposeAsync();
        Directory.Delete(world.WorldFolderPath, recursive: true);
        PluginDimensions.GiveBack("minecraft:overworld");
    }
}
