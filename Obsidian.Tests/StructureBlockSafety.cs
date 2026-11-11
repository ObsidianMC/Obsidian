using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Commands;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.Commands;
using Obsidian.Commands.Framework;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.Utilities;
using Obsidian.WorldData;
using Obsidian.WorldData.Structures;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>Transport, persistence and worker IO risks that a vanilla fixture cannot exercise.</summary>
public sealed class StructureBlockSafety
{
    private static readonly Vector target = new(8, 64, 8);
    private const BlockUpdateFlags Quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;

    [Fact]
    public async Task EditorDataReachesClientsAndSurvivesChunkSerialization()
    {
        await using var context = await StartAsync();
        var world = context.World;
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Editor",
            new VectorD(8.5, 64, 5.5));
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, player);
        player.LoadedChunks.Add(NumericsHelper.IntsToLong(0, 0));
        ((TestClient)player.Client).ClearPackets();

        var settings = new StructureBlockSettings
        {
            Name = "test:editor",
            Mode = StructureBlockMode.Data,
            Metadata = "loot:marker",
            Offset = new Vector(-2, 3, 4),
            Size = new Vector(5, 6, 7),
            Rotation = StructureRotation.Clockwise90,
            Mirror = StructureMirror.FrontBack,
            IgnoreEntities = false,
            Strict = true,
            ShowAir = true,
            ShowBoundingBox = false,
            Integrity = .75f,
            Seed = -123
        };
        await StructureBlocks.EditAsync(world, target, settings, 0, settings.Name);
        await world.DoWorldTickAsync();

        var entity = Assert.IsType<StructureBlockEntity>(world.GetResidentBlockEntity(target));
        var data = BlockEntityNbt.SaveData(entity);
        var packet = Assert.Single(((TestClient)player.Client).SentPackets.OfType<BlockEntityDataPacket>(),
            packet => packet.Position == target && packet.Type == BlockEntityTypesRegistry.NetworkId(entity.Id));
        Assert.Equal(Snbt.Write(data), Snbt.Write(packet.NBTData));

        var saved = BlockEntityNbt.Save(entity)!;
        var restored = Assert.IsType<StructureBlockEntity>(BlockEntityNbt.Load(saved, entity.Id, target));
        Assert.Equal(Snbt.Write(saved), Snbt.Write(BlockEntityNbt.Save(restored)!));
        Assert.Equal("data", world.BlockUpdates.GetBlock(target).GetProperty("mode"));
    }

    [Fact]
    public async Task SaveWaitsForItsWorkerWithoutWorldLocksAndKeepsItsSnapshot()
    {
        await using var context = await StartAsync();
        var world = context.World;
        var source = target + new Vector(1, 1, 1);
        world.BlockUpdates.SetBlock(source, BlocksRegistry.Stone, Quiet);
        var templates = StructureBlockTemplates.For(world);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        typeof(StructureBlockTemplates).GetField("pendingWrites", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(templates, release.Task);
        var settings = new StructureBlockSettings
        {
            Name = "test:snapshot",
            Mode = StructureBlockMode.Save,
            Offset = new Vector(1, 1, 1),
            Size = new Vector(1, 1, 1)
        };

        // The predecessor is an explicit gate: no wall-clock delays or IO-speed assumptions.
        var saving = StructureBlocks.EditAsync(world, target, settings, 1, settings.Name);
        try
        {
            Assert.NotNull(templates.Cached(settings.Name));
            Assert.False(saving.IsCompleted);
            await Task.Run(() =>
            {
                lock (world.Fluids.TickLock)
                {
                    using (world.TransferLock.Enter())
                    {
                        var chunk = Assert.IsType<Chunk>(world.GetLoadedChunk(0, 0));
                        lock (chunk.EntityLock)
                            Assert.NotNull(chunk.GetBlockEntity(target.X, target.Y, target.Z));
                    }

                    world.BlockUpdates.SetBlock(source, BlocksRegistry.Dirt, Quiet);
                }
            });
        }
        finally
        {
            release.TrySetResult();
            await saving;
        }

        Assert.Equal("Structure saved as 'test:snapshot'", await saving);
        templates.Remove(settings.Name);
        var fromDisk = await templates.ReadAsync(settings.Name);
        Assert.NotNull(fromDisk);
        Assert.Single(fromDisk.FilterBlocks(Vector.Zero, new StructurePlaceSettings(), BlockIds.Stone));
        Assert.True(world.BlockUpdates.GetBlock(source).Is(BlockIds.Dirt));
    }

    [Fact]
    public async Task PlaceCommandReadsTheStructureBlocksGeneratedFile()
    {
        await using var context = await StartAsync();
        var world = context.World;
        world.BlockUpdates.SetBlock(target + Vector.Up, BlocksRegistry.Stone, Quiet);
        var settings = new StructureBlockSettings
        {
            Name = "test:command/template",
            Mode = StructureBlockMode.Save,
            Size = new Vector(1, 1, 1)
        };
        Assert.Equal("Structure saved as 'test:command/template'",
            await StructureBlocks.EditAsync(world, target, settings, 1, settings.Name));
        StructureBlockTemplates.For(world).Remove(settings.Name);
        var handler = (CommandHandler)context.Server.CommandHandler;
        handler.RegisterCommandClass<Obsidian.Commands.Modules.PlaceCommandModule>(null);
        var command = new CommandContext("/place template test:command/template 3 64 3 clockwise_90 front_back 1 42",
            new CommandSender(CommandIssuers.Console, null), null, context.Server);

        await handler.ProcessCommand(command);

        Assert.True(command.Succeeded);
        Assert.Equal(1, command.Result);
        Assert.True(world.BlockUpdates.GetBlock(new Vector(3, 64, 3)).Is(BlockIds.Stone));
    }

    private static async Task<RealServerContext> StartAsync()
    {
        var context = TestServerFactory.CreateServer($"structure-block-safety-{Guid.NewGuid():N}");
        context.World.LevelData.GameRules["random_tick_speed"] = "0";
        context.World.LevelData.GameRules["spawn_mobs"] = "false";
        for (var x = -1; x <= 1; x++)
        {
            for (var z = -1; z <= 1; z++)
                WorldBlockEntities.AddEmptyChunk(context.World, x, z);
        }

        await context.World.GetChunkAsync(0, 0, scheduleGeneration: false);
        context.World.BlockUpdates.SetBlock(target, BlocksRegistry.StructureBlock, Quiet);
        return context;
    }
}
