using System;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Registries;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.ChunkData;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Leaves through the vanilla <c>LeavesBlock</c> overrides (<c>randomTick</c>, <c>tick</c>, <c>updateShape</c> and
/// <c>getStateForPlacement</c>), generated into the owner tables.
/// </summary>
public sealed class LeavesDecay
{
    private const int Ground = 69;

    private static readonly Vector Leaf = new(8, Ground + 2, 8);

    [Fact(DisplayName = "Leaves far from any log decay on a random tick, and persistent leaves stay",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task DistantLeavesDecayAndPersistentLeavesStay()
    {
        await using var context = TestServerFactory.CreateServer($"leaves-decay-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);
        var random = new LegacyRandomSource(1);

        Held(world).SetBlock(Leaf, Leaves(distance: 7, persistent: false));
        RandomTickBehaviors.LeavesBlock.RandomTick(world, Leaf, Held(world).GetBlock(Leaf), random);
        await WaitForAirAsync(world, Leaf);

        Held(world).SetBlock(Leaf, Leaves(distance: 7, persistent: true));
        RandomTickBehaviors.LeavesBlock.RandomTick(world, Leaf, Held(world).GetBlock(Leaf), random);
        for (var tick = 0; tick < 5; tick++)
            await world.DoWorldTickAsync();
        Assert.Equal(BlockIds.OakLeaves, Held(world).GetBlock(Leaf).RegistryId);
    }

    private static IBlock Leaves(int distance, bool persistent) => BlocksRegistry.GetDefault(BlockIds.OakLeaves)
        .WithProperty("distance", distance.ToString())
        .WithProperty("persistent", persistent ? "true" : "false");

    private static async Task WaitForAirAsync(WorldData.World world, Vector position)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!Held(world).GetBlock(position).IsAir)
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Leaves at {position} did not decay.");

            await world.DoWorldTickAsync();
        }
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
