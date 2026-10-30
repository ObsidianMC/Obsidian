using System;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.ChunkData;
using Obsidian.Registries;
using Obsidian.WorldData.Features;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Plants that need a block under them (sugar cane, cactus): breaking the block under a stack breaks every plant above
/// it, one tick each, as vanilla's canSurvive checks do.
/// </summary>
public sealed class PlantSurvival
{
    private const int Ground = 69;

    private static readonly Vector Base = new(8, Ground + 1, 8);

    [Theory(DisplayName = "Breaking the bottom of a stack breaks every plant above it", Timeout = TestServerFactory.TestTimeout)]
    [InlineData(BlockIds.SugarCane, 3, true)]
    [InlineData(BlockIds.Bamboo, 3, false)]
    [InlineData(BlockIds.Cactus, 2, false)]
    public async Task BreakingTheBottomBreaksTheStack(int plant, int height, bool needsWater)
    {
        await using var context = TestServerFactory.CreateServer($"plant-stack-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);
        // Chunks only tick while something simulates them; a chunk ticket keeps this one ticking.
        using var ticket = world.AddChunkTicket(0, 0);

        // Sand (with water beside it for sugar cane): the bottom plant can stand there.
        Held(world).SetBlock(Base + Vector.Down, BlocksRegistry.GetDefault(BlockIds.Sand));
        if (needsWater)
            Held(world).SetBlock(Base + Vector.Down + Vector.East, BlocksRegistry.GetDefault(BlockIds.Water));
        for (var y = 0; y < height; y++)
            Held(world).SetBlock(Base + new Vector(0, y, 0), BlocksRegistry.GetDefault(plant));

        Assert.True(world.BlockUpdates.RemoveBlock(Base));
        for (var tick = 0; tick < 3; tick++)
            await world.DoWorldTickAsync();

        for (var y = 0; y < height; y++)
            Assert.True(world.BlockUpdates.GetBlock(Base + new Vector(0, y, 0)).IsAir);
    }

    private static IChunk Held(WorldData.World world) => world.GetLoadedChunk(0, 0)!;

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
                        chunk.SetBlock(chunkX * 16 + x, Ground, chunkZ * 16 + z, BlocksRegistry.GrassBlock);
                }

                chunk.SetChunkStatus(ChunkGenStage.full);
                world.LoadRegionByChunk(chunkX, chunkZ).SetChunk(chunk);
                world.Fluids.Track(chunk);
            }
        }
    }
}
