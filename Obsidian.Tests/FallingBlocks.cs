using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class FallingBlocks
{
    [Fact(Timeout = TestServerFactory.TestTimeout, DisplayName = "Digging the bottom of a sand pillar drops the sand above it instead of filling the gap at once")]
    public async Task SandAboveDugBlockFallsBeforeLanding()
    {
        await using var context = TestServerFactory.CreateServer($"fall-{Guid.NewGuid():N}");
        var world = context.World;
        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
            {
                var chunk = new Obsidian.WorldData.Chunk(chunkX, chunkZ);
                for (var x = 0; x < 16; x += 4)
                {
                    for (var z = 0; z < 16; z += 4)
                    {
                        for (var y = -64; y < 320; y += 4)
                            chunk.SetBiome(x, y, z, CodecRegistry.Biomes.Plains);
                    }
                }

                chunk.SetChunkStatus(ChunkGenStage.full);
                world.LoadRegionByChunk(chunkX, chunkZ).SetChunk(chunk);
                world.Fluids.Track(chunk);
            }
        }

        // Chunks only tick while a player is near them.
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Digger", new VectorD(0.5, 64, 0.5));
        Assert.True(world.TryAddPlayer(player));
        Assert.True(world.TryAddEntity(player));

        var column = new Vector(0, 60, 0);
        await world.SetBlockAsync(column + Vector.Down, BlocksRegistry.Stone);
        for (var y = 0; y < 4; y++)
            await world.SetBlockAsync(column + new Vector(0, y, 0), BlocksRegistry.Sand);

        // Digging the bottom block: the sand above it starts to fall, two ticks after the change, and is still in the air.
        await world.SetBlockAsync(column, BlocksRegistry.Air, true);
        for (var tick = 0; tick < 3; tick++)
            await world.DoWorldTickAsync();

        Assert.Equal(BlocksRegistry.Air.UnlocalizedName, (await world.GetBlockAsync(column))!.UnlocalizedName);
        var falling = Assert.Single(world.GetNonPlayerEntitiesInRange(new VectorD(0.5, 61, 0.5), 5).OfType<FallingBlock>());
        Assert.True(falling.Position.Y is > 60.5 and < 61.0, $"falling block should still be in the air, at {falling.Position.Y}");

        // It stays in the gap's air until it reaches the stone's top, then lands in the gap.
        for (var tick = 0; tick < 60 && world.GetNonPlayerEntitiesInRange(falling.Position, 3).Contains(falling); tick++)
        {
            await world.DoWorldTickAsync();
            if (world.GetNonPlayerEntitiesInRange(falling.Position, 3).Contains(falling))
                Assert.Equal(BlocksRegistry.Air.UnlocalizedName, (await world.GetBlockAsync(column))!.UnlocalizedName);
        }

        Assert.Equal(BlocksRegistry.Sand.UnlocalizedName, (await world.GetBlockAsync(column))!.UnlocalizedName);
        Assert.Contains(((TestClient)player.Client).SentPackets.OfType<Net.Packets.Play.Clientbound.AddTransientBlockPacket>(),
            packet => packet.Position == column && packet.BlockState == BlocksRegistry.Sand.DefaultId);
    }

    [Fact(DisplayName = "Sand landing on a torch drops as an item and leaves the torch in place",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task SandLandingOnTorchDropsItAndKeepsTorch()
    {
        await using var context = TestServerFactory.CreateServer($"fall-torch-{Guid.NewGuid():N}");
        var world = context.World;
        AddFlatChunks(world);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Digger", new VectorD(0.5, 64, 0.5));
        Assert.True(world.TryAddPlayer(player));
        Assert.True(world.TryAddEntity(player));

        // A torch on the stone, with sand two blocks above it: the sand falls through the torch's cell.
        var torch = new Vector(0, 60, 0);
        await world.SetBlockAsync(torch + Vector.Down, BlocksRegistry.Stone);
        await world.SetBlockAsync(torch, BlocksRegistry.GetDefault(BlockIds.Torch));
        await world.SetBlockAsync(torch + (0, 2, 0), BlocksRegistry.Sand, true);

        for (var tick = 0; tick < 20; tick++)
            await world.DoWorldTickAsync();

        Assert.Equal(BlocksRegistry.GetDefault(BlockIds.Torch).UnlocalizedName, (await world.GetBlockAsync(torch))!.UnlocalizedName);
        Assert.Equal(BlocksRegistry.Air.UnlocalizedName, (await world.GetBlockAsync(torch + Vector.Up))!.UnlocalizedName);
        Assert.Empty(world.GetNonPlayerEntitiesInRange(new VectorD(0.5, 61, 0.5), 5).OfType<FallingBlock>());
        var dropped = Assert.Single(world.GetNonPlayerEntitiesInRange(new VectorD(0.5, 60, 0.5), 3).OfType<ItemEntity>());
        Assert.Equal(ItemsRegistry.Sand, dropped.Item.Holder);
    }

    private static void AddFlatChunks(WorldData.World world)
    {
        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
            {
                var chunk = new Obsidian.WorldData.Chunk(chunkX, chunkZ);
                for (var x = 0; x < 16; x += 4)
                {
                    for (var z = 0; z < 16; z += 4)
                    {
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
