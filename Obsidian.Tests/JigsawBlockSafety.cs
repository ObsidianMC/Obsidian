#nullable enable
using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Registries;
using Obsidian.API.World;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.Commands;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.Utilities;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Features;
using Obsidian.WorldData.Structures;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>Checks the transport and persistence that the native jigsaw probe cannot observe.</summary>
[Collection(nameof(PluginTagFiles))]
public sealed class JigsawBlockSafety
{
    [Fact]
    public async Task LegacyJointDefaultsUseChunkStateBeforeItsFirstTick()
    {
        using var fixture = JsonDocument.Parse(typeof(JigsawBlockSafety).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.jigsaw_blocks.json")!);
        var references = fixture.RootElement.GetProperty("cases").EnumerateArray()
            .Where(test => test.GetProperty("name").GetString() is
                "load_missing_horizontal" or "load_missing_vertical");
        foreach (var reference in references)
        {
            var folder = Path.Join(Path.GetTempPath(), $"obsidian-jigsaw-save-{Guid.NewGuid():N}");
            var position = new Vector(0, 80, 0);
            try
            {
                // Write legacy data through a raw entity so the save really omits the joint field.
                await using (var region = new Region(0, 0, folder, "region"))
                {
                    await region.InitAsync();
                    var chunk = new Chunk(0, 0);
                    var state = reference.GetProperty("setup")[0].GetProperty("state").GetString()!;
                    chunk.SetBlock(position.X, position.Y, position.Z, VanillaSulfurBlocks.ParseState(state));
                    chunk.SetBlockEntity(position.X, position.Y, position.Z, new DataBlockEntity
                    {
                        Id = JigsawBlockEntity.TypeId,
                        BlockPosition = position
                    });
                    chunk.SetChunkStatus(ChunkGenStage.full);
                    region.SetChunk(chunk);
                    await region.FlushAsync();
                }

                await using (var region = new Region(0, 0, folder, "region"))
                {
                    await region.InitAsync();
                    var chunk = Assert.IsType<Chunk>(await region.GetChunkAsync(0, 0));
                    var entity = Assert.IsType<JigsawBlockEntity>(chunk.GetBlockEntity(0, 80, 0));
                    Assert.Null(entity.Level);
                    var expected = JsonNode.Parse(reference.GetProperty("checkpoints")[0]
                        .GetProperty("world").GetProperty("updates").GetProperty("0,0,0").GetRawText());
                    var actual = VanillaJigsawBlocks.Canonical(entity.GetUpdateData());
                    Assert.True(JsonNode.DeepEquals(expected, actual), $"expected {expected}, got {actual}");
                }
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerationLoadsReachedChunksWithoutWorldLocksAndRejectsStaleEditors()
    {
        using var fixture = JsonDocument.Parse(typeof(JigsawBlockSafety).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.jigsaw_blocks.json")!);
        var root = fixture.RootElement;
        var reference = root.GetProperty("cases").EnumerateArray()
            .Single(test => test.GetProperty("name").GetString() == "generate_across_chunk_edge");
        var origin = new Vector(0, 80, 0);
        var features = new FeatureRegistry(WorldgenData.Vanilla);
        foreach (var stale in new[] { false, true })
        {
            await using var context = TestServerFactory.CreateServer($"jigsaw-chunks-{Guid.NewGuid():N}");
            var world = context.World;
            world.LevelData.RandomSeed = root.GetProperty("seed").GetInt64();
            WorldBlockEntities.AddEmptyChunk(world);
            var templates = VanillaJigsawBlocks.BuildTemplates(world, origin, root);
            var files = root.GetProperty("pools").EnumerateObject().Select(pool =>
                new StructureFile(PluginStructures.TemplatePool, pool.Name, "jigsaw capture", pool.Value)).ToArray();
            PluginStructures.Apply(files, templates, features.AsFeatureSet(), [], NullLogger.Instance);
            try
            {
                VanillaJigsawBlocks.Place(world, origin, reference.GetProperty("setup")[0]);
                var editorPosition = origin + new Vector(15, 0, 0);
                var generator = new GatedGenerator();
                world.Generator = generator;
                var packet = VanillaJigsawBlocks.GeneratePacket(editorPosition,
                    reference.GetProperty("actions")[0].GetProperty("generate"));
                var generating = JigsawBlocks.GenerateAsync(world, editorPosition, packet.Levels, packet.KeepJigsaws);
                var reached = await Task.WhenAny(generator.Entered.Task, generating);
                Assert.Same(generator.Entered.Task, reached);
                try
                {
                    // This lock must be available while generation is suspended at its explicit gate.
                    await Task.Run(() =>
                    {
                        lock (world.Fluids.TickLock)
                        {
                            if (stale)
                            {
                                var block = world.BlockUpdates.GetBlock(editorPosition);
                                world.BlockUpdates.SetBlock(editorPosition, block.WithProperty("orientation", "up_north"),
                                    BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
                            }
                        }
                    });
                }
                finally
                {
                    generator.Release.TrySetResult();
                }

                await generating;
                Assert.Equal(new[] { (X: 1, Z: 0) }, generator.Requested);
                if (stale)
                {
                    Assert.True(world.BlockUpdates.GetBlock(origin + new Vector(16, -1, 0)).IsAir);
                }
                else
                {
                    var actual = VanillaJigsawBlocks.Checkpoint(world, origin,
                        new Vector(-2, -2, -12), new Vector(24, 4, 16));
                    var expected = JsonNode.Parse(reference.GetProperty("checkpoints")[0]
                        .GetProperty("world").GetRawText());
                    Assert.True(JsonNode.DeepEquals(expected, actual), $"expected {expected}, got {actual}");
                }
            }
            finally
            {
                PluginStructures.Apply([], [], features.AsFeatureSet(), [], NullLogger.Instance);
            }
        }
    }

    [Fact]
    public async Task EditedDataReachesClientsAndSurvivesChunkSerialization()
    {
        await using var context = TestServerFactory.CreateServer($"jigsaw-sync-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        var target = new Vector(8, 80, 8);
        world.BlockUpdates.SetBlock(target, BlocksRegistry.Jigsaw,
            BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Editor",
            new VectorD(8.5, 80, 5.5));
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, player);
        player.LoadedChunks.Add(NumericsHelper.IntsToLong(0, 0));
        player.GameMode = GameMode.Creative;
        VanillaJigsawBlocks.MakeOperator(context.Server, player);
        await world.DoWorldTickAsync();
        ((TestClient)player.Client).ClearPackets();

        using var fixture = JsonDocument.Parse(typeof(JigsawBlockSafety).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.jigsaw_blocks.json")!);
        var reference = fixture.RootElement.GetProperty("cases").EnumerateArray()
            .Single(test => test.GetProperty("name").GetString() == "edit_and_reload");
        var edit = reference.GetProperty("actions")[0].GetProperty("edit");
        var entity = Assert.IsType<JigsawBlockEntity>(world.GetResidentBlockEntity(target));
        await VanillaJigsawBlocks.EditPacket(target, edit).HandleAsync(context.Server, player);
        await world.DoWorldTickAsync();

        var data = entity.GetUpdateData();
        var packet = Assert.Single(((TestClient)player.Client).SentPackets.OfType<BlockEntityDataPacket>(),
            packet => packet.Position == target && packet.Type == BlockEntityTypesRegistry.NetworkId(entity.Id));
        Assert.Equal(Snbt.Write(data), Snbt.Write(packet.NBTData));
        var saved = BlockEntityNbt.Save(entity)!;
        var restored = Assert.IsType<JigsawBlockEntity>(BlockEntityNbt.Load(saved, entity.Id, target));
        Assert.Equal(Snbt.Write(saved), Snbt.Write(BlockEntityNbt.Save(restored)!));
    }

    private sealed class GatedGenerator : ILevelGenerator
    {
        public string Id => "jigsaw-test-gate";

        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal ConcurrentQueue<(int X, int Z)> Requested { get; } = new();

        public void Init(ILevel level)
        {
        }

        public async ValueTask<IChunk> GenerateChunkAsync(int x, int z, IChunk? chunk = null,
            ChunkGenStage status = ChunkGenStage.full)
        {
            this.Requested.Enqueue((x, z));
            this.Entered.TrySetResult();
            await this.Release.Task;
            chunk ??= new Chunk(x, z);
            chunk.SetChunkStatus(ChunkGenStage.full);
            return chunk;
        }
    }
}
