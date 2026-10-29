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

    [Fact(DisplayName = "A sugar cane with air above grows one age on a random tick", Timeout = TestServerFactory.TestTimeout)]
    public async Task SugarCaneGrowsOnRandomTick()
    {
        await using var context = TestServerFactory.CreateServer($"plant-cane-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);
        var random = new LegacyRandomSource(1);

        Held(world).SetBlock(Base + Vector.Down, BlocksRegistry.GetDefault(BlockIds.Dirt));
        Held(world).SetBlock(Base, BlocksRegistry.GetDefault(BlockIds.SugarCane).WithProperty("age", "0"));
        RandomTickBehaviors.SugarCaneBlock.RandomTick(world, Base, Held(world).GetBlock(Base), random);

        Assert.Equal("1", Held(world).GetBlock(Base).GetProperty("age"));
        await world.DoWorldTickAsync();
    }

    [Fact(DisplayName = "A nether wart below age 3 grows only on a draw of 0 out of 10", Timeout = TestServerFactory.TestTimeout)]
    public async Task NetherWartAgesOnlyWhenTheDrawComesUpZero()
    {
        await using var context = TestServerFactory.CreateServer($"plant-wart-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);

        Held(world).SetBlock(Base, BlocksRegistry.GetDefault(BlockIds.NetherWart).WithProperty("age", "3"));
        var random = new LegacyRandomSource(1);
        for (var tick = 0; tick < 50; tick++)
            RandomTickBehaviors.NetherWartBlock.RandomTick(world, Base, Held(world).GetBlock(Base), random);

        Assert.Equal("3", Held(world).GetBlock(Base).GetProperty("age"));
        await world.DoWorldTickAsync();
    }

    [Fact(DisplayName = "A chorus flower on end stone with air above grows a stalk and a new flower", Timeout = TestServerFactory.TestTimeout)]
    public async Task ChorusFlowerGrowsOnEndStone()
    {
        await using var context = TestServerFactory.CreateServer($"plant-chorus-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);
        var random = new LegacyRandomSource(1);

        Held(world).SetBlock(Base + Vector.Down, BlocksRegistry.GetDefault(BlockIds.EndStone));
        Held(world).SetBlock(Base, BlocksRegistry.ChorusFlower.WithProperty("age", 0));
        RandomTickBehaviors.ChorusFlowerBlock.RandomTick(world, Base, Held(world).GetBlock(Base), random);

        Assert.Equal(BlockIds.ChorusPlant, Held(world).GetBlock(Base).RegistryId);
        Assert.Equal(BlockIds.ChorusFlower, Held(world).GetBlock(Base + Vector.Up).RegistryId);
        Assert.Equal("0", Held(world).GetBlock(Base + Vector.Up).GetProperty("age"));
        await world.DoWorldTickAsync();
    }

    [Fact(DisplayName = "Grass under an opaque block decays to dirt on a random tick", Timeout = TestServerFactory.TestTimeout)]
    public async Task GrassDecaysUnderOpaqueBlock()
    {
        await using var context = TestServerFactory.CreateServer($"plant-grass-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);

        var grass = Base + Vector.Down;
        Held(world).SetBlock(Base, BlocksRegistry.GetDefault(BlockIds.Stone));
        Assert.Equal(BlockIds.GrassBlock, Held(world).GetBlock(grass).RegistryId);
        RandomTickBehaviors.SpreadingSnowyBlock.RandomTick(world, grass, Held(world).GetBlock(grass), new LegacyRandomSource(1));

        Assert.Equal(BlockIds.Dirt, Held(world).GetBlock(grass).RegistryId);
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
