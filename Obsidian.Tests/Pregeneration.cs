using Obsidian.API;
using Obsidian.API.Configuration;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.Tests.Fakes;
using Obsidian.Utilities;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Pregeneration stages the Mojang generator's area a tile at a time, saving each tile and unloading the chunks no
/// tile left needs.
/// </summary>
[Trait("Category", TestCategory.Worldgen)]
public sealed class Pregeneration
{
    // Generating the area takes most of TestServerFactory.TestTimeout in a debug build on its own, so the parallel
    // worldgen replays could push it past that; the timeout only has to catch a hang.
    private const int GenerationTimeout = 4 * TestServerFactory.TestTimeout;

    [Fact(Timeout = GenerationTimeout)]
    public async Task PregeneratedChunksAreSavedAndUnloaded()
    {
        const int range = 2;
        var configuration = new ServerConfiguration { SpawnChunkRadius = 1, TimeTickSpeedMultiplier = 1, PregenerateChunkRange = range };
        var world = new World(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<World>.Instance,
            new FakeWorldManager(defaultWorld: null!),
            new FakePacketBroadcaster(),
            new TestOptionsMonitor<ServerConfiguration>(configuration),
            new FakeEventDispatcher(),
            new MojangGenerator(),
            $"pregeneration-{Guid.NewGuid():N}",
            "12345");
        world.Initialize(new DimensionCodec
        {
            Name = "minecraft:overworld",
            Id = 0,
            Element = new DimensionElement
            {
                MonsterSpawnBlockLightLimit = 0,
                MonsterSpawnLightLevel = new MonsterSpawnLightLevel { IntValue = 0 },
                AmbientLight = 0,
                HasSkylight = true,
                MinY = -64,
                Height = 384,
                LogicalHeight = 384,
                CoordinateScale = 1,
                HasCeiling = false
            }
        });

        try
        {
            await world.GenerateAsync();

            // Done with every tile, only the spawn chunks stay loaded, and every chunk of the area comes back complete
            // from disk.
            var (centerX, centerZ) = world.LevelData.SpawnPosition.ToChunkCoord();
            long[] spawnChunks =
            [
                NumericsHelper.IntsToLong(centerX - 1, centerZ - 1), NumericsHelper.IntsToLong(centerX - 1, centerZ),
                NumericsHelper.IntsToLong(centerX, centerZ - 1), NumericsHelper.IntsToLong(centerX, centerZ)
            ];
            Assert.Equal(spawnChunks.Order(), world.LoadedChunks.Order());
            for (var x = centerX - range; x < centerX + range; x++)
            {
                for (var z = centerZ - range; z < centerZ + range; z++)
                    Assert.True((await world.GetChunkAsync(x, z, scheduleGeneration: false))!.IsGenerated);
            }
        }
        finally
        {
            await world.DisposeAsync();
            Directory.Delete(world.WorldFolderPath, recursive: true);

            // The world's generator took the overworld's stem, which data pack startup tests expect free.
            Obsidian.Registries.PluginDimensions.GiveBack("minecraft:overworld");
        }
    }
}
