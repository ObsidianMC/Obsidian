using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.Entities;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.API.World;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.ChunkData;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.Utilities;
using Obsidian.WorldData;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Smoke test of crop growth: a random tick grows the wheat it reaches, and the client is shown the change. Also
/// guards the tick's cached neighbour check, which must forget a chunk's neighbours once one of them unloads.
/// </summary>
public sealed class CropGrowth
{
    private const int Ground = 69;

    private const int Seed = 0x5eed;

    [Fact(DisplayName = "A random tick in a loaded section grows the wheat it reaches and shows the client")]
    public async Task RandomTicksGrowWheatAndShowIt()
    {
        await using var context = TestServerFactory.CreateServer($"crop-flow-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);
        var chunk = world.GetLoadedChunk(0, 0)!;
        PlantWheat(chunk);

        world.GrowthRandom = new LegacyRandomSource(Seed);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Grower", new VectorD(8, Ground + 1, 8));
        player.GameMode = GameMode.Survival;
        Assert.True(world.TryAddPlayer(player));

        // The chunks the player has been sent, so that block changes in them reach the client.
        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
                player.LoadedChunks.Add(NumericsHelper.IntsToLong(chunkX, chunkZ));
        }
        var client = (TestClient)player.Client;
        client.ClearPackets();

        for (var tick = 0; tick < 1000; tick++)
            world.TickRandomBlocks();

        // The level may have replaced the chunk object while ticking, so read the chunk the level holds now.
        chunk = world.GetLoadedChunk(0, 0)!;

        // Each growth went out through the block-change path, at the level's flush, as the block the chunk holds then.
        world.FlushBlockChanges();
        var updates = BlockUpdates.SentBlocks(client).ToList();
        Assert.NotEmpty(updates);
        Assert.All(updates, update => Assert.True(chunk.GetBlock(update.Position).Is(BlockIds.Wheat)));
        Assert.All(updates, update => Assert.Equal(chunk.GetBlock(update.Position).GetHashCode(), update.StateId));
    }

    // The ways a neighbour leaves the level's loaded chunks after random ticks remembered the chunk as complete.
    private sealed record NeighbourLoss(string Name, Func<WorldData.World, Task> Lose);

    [Fact(DisplayName = "Random ticks stop reaching a chunk once one of its neighbours is no longer loaded")]
    public async Task LostNeighbourStopsRandomTicks()
    {
        NeighbourLoss[] losses =
        [
            new("unloaded", world => world.LoadRegionByChunk(1, 1).UnloadChunk(1, 1)),
            new("no longer complete", world =>
            {
                ((Chunk)world.GetLoadedChunk(1, 1)!).SetChunkStatusCore(ChunkGenStage.light);
                return Task.CompletedTask;
            })
        ];

        await FixtureCases.AssertAllAsync(losses, loss => loss.Name, AssertLostNeighbourStopsRandomTicksAsync);
    }

    private static async Task AssertLostNeighbourStopsRandomTicksAsync(NeighbourLoss loss)
    {
        await using var context = TestServerFactory.CreateServer($"crop-neighbours-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);
        PlantWheat(world.GetLoadedChunk(0, 0)!);
        world.GrowthRandom = new LegacyRandomSource(Seed);

        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Grower", new VectorD(8, Ground + 1, 8));
        player.GameMode = GameMode.Survival;
        Assert.True(world.TryAddPlayer(player));

        // With all eight neighbours loaded, the tick's random ticks reach the wheat, and the chunk is remembered
        // as complete.
        Assert.True(GrowsDuringTicks(world));

        // One neighbour leaves the loaded chunks, so the chunk must be checked again rather than taken as complete.
        PlantWheat(world.GetLoadedChunk(0, 0)!);
        await loss.Lose(world);
        Assert.Null(world.GetLoadedChunk(1, 1));

        Assert.False(GrowsDuringTicks(world));
    }

    // Ticks random blocks as the level's tick does, with its simulation snapshot, and reports whether any wheat in
    // chunk 0, 0 grew.
    private static bool GrowsDuringTicks(WorldData.World world)
    {
        world.SnapshotPlayers();
        for (var tick = 0; tick < 200; tick++)
            world.TickRandomBlocks();

        var chunk = world.GetLoadedChunk(0, 0)!;
        for (var x = 0; x < 16; x++)
        {
            for (var y = 64; y < 80; y++)
            {
                for (var z = 0; z < 16; z++)
                {
                    if (chunk.GetBlock(x, y, z).GetProperty("age") != "0")
                        return true;
                }
            }
        }

        return false;
    }

    // Fills one section of the chunk with young wheat in full light, so every random tick there reaches a crop.
    private static void PlantWheat(IChunk chunk)
    {
        for (var x = 0; x < 16; x++)
        {
            for (var y = 64; y < 80; y++)
            {
                for (var z = 0; z < 16; z++)
                    chunk.SetBlock(x, y, z, BlocksRegistry.GetDefault(BlockIds.Wheat));
            }
        }

        // Light after the blocks, since block writes update the light around them.
        for (var x = 0; x < 16; x++)
        {
            for (var y = 64; y < 80; y++)
            {
                for (var z = 0; z < 16; z++)
                    chunk.SetLightLevel(x, y, z, LightType.Sky, 15);
            }
        }
    }

    private static void AddChunks(WorldData.World world)
    {
        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
            {
                var chunk = new Chunk(chunkX, chunkZ);

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
            }
        }
    }
}
