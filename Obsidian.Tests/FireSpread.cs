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
/// Smoke test of fire: its scheduled tick spreads to a flammable log beside it.
/// </summary>
public sealed class FireSpread
{
    private const int Ground = 69;

    private static readonly Vector Base = new(8, Ground + 1, 8);

    [Fact(DisplayName = "Fire beside an oak log sets it alight over its scheduled ticks", Timeout = TestServerFactory.TestTimeout)]
    public async Task FireSetsALogAlight()
    {
        await using var context = TestServerFactory.CreateServer($"fire-log-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);
        world.LevelData.GameRules["fire_spread_radius_around_player"] = "-1";

        var log = Base + Vector.East;
        Held(world).SetBlock(log, BlocksRegistry.GetDefault(BlockIds.OakLog));
        Held(world).SetBlock(Base, FireBlockBehavior.StateFor(world.BlockUpdates, Base));

        var random = new LegacyRandomSource(7);
        for (var tick = 0; tick < 5000 && Held(world).GetBlock(log).RegistryId == BlockIds.OakLog; tick++)
            TickBehaviors.FireBlock.Tick(world.BlockUpdates, Base, Held(world).GetBlock(Base), random);

        Assert.NotEqual(BlockIds.OakLog, Held(world).GetBlock(log).RegistryId);
        await world.DoWorldTickAsync();
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
