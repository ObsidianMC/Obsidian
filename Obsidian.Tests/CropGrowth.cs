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
using Obsidian.Utilities;
using Obsidian.WorldData;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Smoke test of crop growth: a random tick grows the wheat it reaches, and the client is shown the change.
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

        // Every block of the section is wheat in full light, so every random tick reaches a crop.
        for (var x = 0; x < 16; x++)
        {
            for (var y = 64; y < 80; y++)
            {
                for (var z = 0; z < 16; z++)
                {
                    chunk.SetBlock(x, y, z, BlocksRegistry.GetDefault(BlockIds.Wheat));
                }
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

        // Each growth went out through the block-change path as the block the chunk holds afterwards.
        var updates = client.SentPackets.OfType<BlockUpdatePacket>().ToList();
        Assert.NotEmpty(updates);
        Assert.All(updates, update => Assert.True(chunk.GetBlock(update.Position).Is(BlockIds.Wheat)));
        Assert.Contains(updates, update => update.BlockId == chunk.GetBlock(update.Position).GetHashCode());
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
