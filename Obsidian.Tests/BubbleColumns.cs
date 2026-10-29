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
using Obsidian.WorldData.Generators.Mojang;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Bubble columns that soul sand and magma start under water, and the new column reaching the client.
/// </summary>
public sealed class BubbleColumns
{
    private const int Ground = 69;

    // 26.1 moved starting a column from soul sand's and magma's own hooks to the water above them (LiquidBlock, with
    // the enables_bubble_column_* tags), so their owners no longer bind. See docs/known-gaps.md.
    private const string WaterStartsColumns = "Phase 6: 26.1 moved starting a bubble column from soul sand and magma to the water above";

    [Fact(DisplayName = "Replacing the block beneath existing water with magma starts a downward column", Skip = WaterStartsColumns)]
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

    [Fact(DisplayName = "A player pouring water on soul sand, through the bucket's use, starts a column the viewer is sent", Skip = WaterStartsColumns)]
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
