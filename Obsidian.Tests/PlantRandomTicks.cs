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
/// Plant random ticks of milestone 2 (nether wart, cocoa, sugar cane, cactus, sweet berry bush, bamboo stalk and
/// sapling), generated from the vanilla owner classes.
/// </summary>
public sealed class PlantRandomTicks
{
    private const int Ground = 69;

    private static readonly Vector Base = new(8, Ground + 1, 8);

    [Fact(DisplayName = "Each plant's random tick is owned by its vanilla class")]
    public void OwnersAreGenerated()
    {
        Assert.Same(RandomTickBehaviors.NetherWartBlock, BlocksRegistry.GetRandomTickBehavior(BlockIds.NetherWart));
        Assert.Same(RandomTickBehaviors.CocoaBlock, BlocksRegistry.GetRandomTickBehavior(BlockIds.Cocoa));
        Assert.Same(RandomTickBehaviors.SugarCaneBlock, BlocksRegistry.GetRandomTickBehavior(BlockIds.SugarCane));
        Assert.Same(RandomTickBehaviors.CactusBlock, BlocksRegistry.GetRandomTickBehavior(BlockIds.Cactus));
        Assert.Same(RandomTickBehaviors.SweetBerryBushBlock, BlocksRegistry.GetRandomTickBehavior(BlockIds.SweetBerryBush));
        Assert.Same(RandomTickBehaviors.BambooStalkBlock, BlocksRegistry.GetRandomTickBehavior(BlockIds.Bamboo));
        Assert.Same(RandomTickBehaviors.BambooSaplingBlock, BlocksRegistry.GetRandomTickBehavior(BlockIds.BambooSapling));
        Assert.Same(RandomTickBehaviors.ChorusFlowerBlock, BlocksRegistry.GetRandomTickBehavior(BlockIds.ChorusFlower));
        Assert.Same(RandomTickBehaviors.SpreadingSnowyBlock, BlocksRegistry.GetRandomTickBehavior(BlockIds.GrassBlock));
        Assert.Same(RandomTickBehaviors.SpreadingSnowyBlock, BlocksRegistry.GetRandomTickBehavior(BlockIds.Mycelium));
        Assert.Null(BlocksRegistry.GetRandomTickBehavior(BlockIds.Dirt));
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
