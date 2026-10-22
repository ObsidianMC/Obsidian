using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.World;
using Obsidian.Entities;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.API.Blocks;
using Obsidian.API.Registries;
using Obsidian.ChunkData;
using Obsidian.Registries;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Tests.Fakes;
using Obsidian.Utilities;
using System.Linq;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Mojang;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Bubble columns: the vanilla owners of their overrides, generated into <c>BlocksRegistry</c>: the column's
/// <c>tick</c>, <c>updateShape</c> and <c>entityInside</c>, and soul sand and magma, which start a column above them.
/// </summary>
public sealed class BubbleColumns
{
    private const int Ground = 69;

    [Fact(DisplayName = "Bubble columns and their sources are owned by their vanilla classes' overrides")]
    public void OwnersAreGenerated()
    {
        Assert.Same(TickBehaviors.BubbleColumnBlock, BlocksRegistry.GetTickBehavior(BlockIds.BubbleColumn));
        Assert.Same(ShapeUpdateBehaviors.BubbleColumnBlock, BlocksRegistry.GetShapeUpdateBehavior(BlockIds.BubbleColumn));
        Assert.Same(EntityInsideBehaviors.BubbleColumnBlock, BlocksRegistry.GetEntityInsideBehavior(BlockIds.BubbleColumn));
        Assert.Same(TickBehaviors.SoulSandBlock, BlocksRegistry.GetTickBehavior(BlockIds.SoulSand));
        Assert.Same(ShapeUpdateBehaviors.SoulSandBlock, BlocksRegistry.GetShapeUpdateBehavior(BlockIds.SoulSand));
        Assert.Same(OnPlaceBehaviors.SoulSandBlock, BlocksRegistry.GetOnPlaceBehavior(BlockIds.SoulSand));
        Assert.Same(TickBehaviors.MagmaBlock, BlocksRegistry.GetTickBehavior(BlockIds.MagmaBlock));
        Assert.Same(ShapeUpdateBehaviors.MagmaBlock, BlocksRegistry.GetShapeUpdateBehavior(BlockIds.MagmaBlock));
        Assert.Same(OnPlaceBehaviors.MagmaBlock, BlocksRegistry.GetOnPlaceBehavior(BlockIds.MagmaBlock));
    }

    [Fact(DisplayName = "Only a bubble column takes the entity contact, so other blocks keep their own path")]
    public void OnlyColumnsHaveEntityInside()
    {
        Assert.Null(BlocksRegistry.GetEntityInsideBehavior(BlockIds.SoulSand));
        Assert.Null(BlocksRegistry.GetEntityInsideBehavior(BlockIds.MagmaBlock));
    }

    [Fact(DisplayName = "Water above soul sand becomes an upward column, and above magma a downward one")]
    public async Task WaterAboveSourceBecomesColumn()
    {
        await using var context = TestServerFactory.CreateServer($"bubble-column-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);

        var soul = new Vector(8, Ground, 8);
        var magma = new Vector(10, Ground, 8);
        world.BlockUpdates.SetBlock(soul, BlocksRegistry.SoulSand, BlockUpdateFlags.All);
        world.BlockUpdates.SetBlock(soul + Vector.Up, BlocksRegistry.Water, BlockUpdateFlags.All);
        world.BlockUpdates.SetBlock(magma, BlocksRegistry.MagmaBlock, BlockUpdateFlags.All);
        world.BlockUpdates.SetBlock(magma + Vector.Up, BlocksRegistry.Water, BlockUpdateFlags.All);

        Assert.True(world.BlockUpdates.HasScheduledTick(soul, BlocksRegistry.SoulSand));
        Assert.True(world.BlockUpdates.HasScheduledTick(magma, BlocksRegistry.MagmaBlock));

        world.BlockUpdates.RunScheduledTick(soul, BlocksRegistry.SoulSand.UnlocalizedName);
        world.BlockUpdates.RunScheduledTick(magma, BlocksRegistry.MagmaBlock.UnlocalizedName);

        var upward = world.BlockUpdates.GetBlock(soul + Vector.Up);
        Assert.True(upward.Is(BlockIds.BubbleColumn));
        Assert.Equal("false", upward.GetProperty("drag"));

        var downward = world.BlockUpdates.GetBlock(magma + Vector.Up);
        Assert.True(downward.Is(BlockIds.BubbleColumn));
        Assert.Equal("true", downward.GetProperty("drag"));
    }

    [Fact(DisplayName = "Magma that worldgen placed under water is post-processed into a scheduled column check")]
    public void GeneratedMagmaUnderWaterSchedulesColumnCheck()
    {
        var dimension = MojangDimension.Overworld;
        var builder = new ChunkBuilder(dimension, 12345L, generateStructures: false);
        var area = new Dictionary<(int X, int Z), IChunk>();
        for (var dx = -1; dx <= 1; dx++)
            for (var dz = -1; dz <= 1; dz++)
                area[(dx, dz)] = new Chunk(dx, dz, dimension.MinY, dimension.Height);

        var center = (Chunk)area[(0, 0)];
        var source = new Vector(8, Ground, 8);
        center.SetBlock(source.X, source.Y, source.Z, BlocksRegistry.MagmaBlock);
        center.SetBlock(source.X, source.Y + 1, source.Z, BlocksRegistry.Water);
        // Worldgen marks the magma's position when it writes it, since vanilla's hasPostProcess is true for magma.
        center.PostProcessing.Add(source);

        builder.PostProcess(area, 0, 0);

        Assert.True(center.BlockTicks.Contains(source, BlocksRegistry.MagmaBlock.UnlocalizedName));
    }

    [Fact(DisplayName = "Replacing the block beneath existing water with magma starts a downward column")]
    public async Task MagmaPlacedUnderExistingWaterStartsColumn()
    {
        await using var context = TestServerFactory.CreateServer($"bubble-column-undersea-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);
        var viewer = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Diver", new VectorD(8.5, Ground + 1, 8.5));
        var client = (TestClient)viewer.Client;
        Assert.True(world.TryAddPlayer(viewer));
        for (var chunkX = -1; chunkX <= 1; chunkX++)
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
                viewer.LoadedChunks.Add(NumericsHelper.IntsToLong(chunkX, chunkZ));

        var source = new Vector(8, Ground, 8);
        var water = source + Vector.Up;
        world.BlockUpdates.SetBlock(water, BlocksRegistry.Water, BlockUpdateFlags.All);
        world.BlockUpdates.SetBlock(source, BlocksRegistry.MagmaBlock, BlockUpdateFlags.All);
        client.ClearPackets();

        Assert.True(world.BlockUpdates.HasScheduledTick(source, BlocksRegistry.MagmaBlock));
        for (var tick = 0; tick < 40; tick++)
            world.Fluids.Tick();

        var column = world.BlockUpdates.GetBlock(water);
        Assert.True(column.Is(BlockIds.BubbleColumn), $"server block at {water}: {column.UnlocalizedName}");
        Assert.Equal("true", column.GetProperty("drag"));
        Assert.Contains(
            client.SentPackets.OfType<BlockUpdatePacket>(),
            packet => packet.Position == water && packet.BlockId == column.GetHashCode());
    }

    [Fact(DisplayName = "A player pouring water on soul sand, through the bucket's use, starts a column the viewer is sent")]
    public async Task BucketOnSoulSandStartsColumn()
    {
        await using var context = TestServerFactory.CreateServer($"bubble-column-bucket-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);
        var viewer = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Pourer", new VectorD(8.5, Ground + 1, 8.5));
        var client = (TestClient)viewer.Client;
        Assert.True(world.TryAddPlayer(viewer));
        for (var chunkX = -1; chunkX <= 1; chunkX++)
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
                viewer.LoadedChunks.Add(NumericsHelper.IntsToLong(chunkX, chunkZ));
        viewer.GameMode = GameMode.Survival;
        viewer.Inventory.SetItem(viewer.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.WaterBucket, 1));

        var soul = new Vector(8, Ground, 8);
        world.BlockUpdates.SetBlock(soul, BlocksRegistry.SoulSand, BlockUpdateFlags.All);
        client.ClearPackets();

        await UseOn(soul, BlockFace.Up).HandleAsync(context.Server, viewer);
        for (var tick = 0; tick < 40; tick++)
            world.Fluids.Tick();

        var up = soul + Vector.Up;
        var column = world.BlockUpdates.GetBlock(up);
        var sent = client.SentPackets.OfType<BlockUpdatePacket>().Select(p => $"{p.Position}={p.BlockId}").ToArray();
        Assert.True(column.Is(BlockIds.BubbleColumn), $"server block at {up}: {column.UnlocalizedName}/{column.GetProperty("drag")}; sent [{string.Join("; ", sent)}]");
        Assert.Contains(
            client.SentPackets.OfType<BlockUpdatePacket>(),
            packet => packet.Position == up && packet.BlockId == column.GetHashCode());
    }

    // A player using their main hand on the face of the block at position, at the middle of its top.
    private static UseItemOnPacket UseOn(Vector position, BlockFace face)
    {
        using var writer = new NetworkBuffer();
        writer.WriteVarInt((int)InteractionHand.MainHand);
        writer.WritePosition(position);
        writer.WriteVarInt((int)face);
        writer.WriteAbsoluteFloatPositionF(new VectorF(0.5f, 1, 0.5f));
        writer.WriteBoolean(false);
        writer.WriteBoolean(false);
        writer.WriteVarInt(1);

        using var reader = new NetworkBuffer(writer.AsSpan(0, writer.Size).ToArray());
        var packet = new UseItemOnPacket();
        packet.Populate(reader);
        return packet;
    }

    [Fact(DisplayName = "A bubble column at a survival player's eye refills air, while plain water drains it")]
    public async Task BubbleColumnAtEyeRefillsAir()
    {
        await using var context = TestServerFactory.CreateServer($"bubble-breath-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);

        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Diver");
        player.GameMode = GameMode.Survival;
        player.Position = new VectorD(8.5, Ground + 1, 8.5);
        for (var y = Ground + 1; y <= Ground + 3; y++)
            world.BlockUpdates.SetBlock(new Vector(8, y, 8), BlocksRegistry.Water, BlockUpdateFlags.All);

        // The eye is at 1.62 above the feet, inside the block at Ground + 2, which holds water here.
        player.Air = 100;
        await player.TickAsync();
        Assert.Equal(99, player.Air);

        world.BlockUpdates.SetBlock(new Vector(8, Ground + 2, 8), BlocksRegistry.BubbleColumn.WithProperty("drag", "false"), BlockUpdateFlags.All);
        player.Air = 100;
        await player.TickAsync();
        Assert.Equal(104, player.Air);
    }

    private static void AddChunks(WorldData.World world)
    {
        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
            {
                var chunk = new WorldData.Chunk(chunkX, chunkZ);

                for (var x = 0; x < 16; x++)
                {
                    for (var z = 0; z < 16; z++)
                    {
                        chunk.SetBlock(chunkX * 16 + x, Ground, chunkZ * 16 + z, BlocksRegistry.GrassBlock);

                        if (x % 4 != 0 || z % 4 != 0)
                            continue;

                        for (var y = -64; y < 320; y += 4)
                            chunk.SetBiome(x, y, z, CodecRegistry.Biomes.Plains);
                    }
                }

                chunk.SetChunkStatus(ChunkGenStage.full);
                world.LoadRegionByChunk(chunkX, chunkZ).SetChunk(chunk);
                world.Fluids.Track(chunk);
            }
        }
    }
}
