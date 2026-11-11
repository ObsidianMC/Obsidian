using System;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.ChunkData;
using Obsidian.Entities;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// The smoke test of sculk catalysts through a real mob's death. What a catalyst does with the charge is replayed
/// against vanilla in <c>ticking_blocks.json</c> (<see cref="VanillaTickingBlocks"/>).
/// </summary>
public sealed class SculkCatalysts
{
    [Fact]
    public async Task MobDyingBesideACatalystFeedsItsCharge()
    {
        await using var context = TestServerFactory.CreateServer($"sculk-catalyst-{Guid.NewGuid():N}");
        var world = context.World;
        AddFloor(world);
        world.GrowthRandom = new LegacyRandomSource(0);

        var catalystPosition = new Vector(8, 64, 8);
        world.BlockUpdates.SetBlock(catalystPosition, BlocksRegistry.SculkCatalyst);
        var catalyst = Assert.IsType<SculkCatalystBlockEntity>(world.GetResidentBlockEntity(catalystPosition));

        // A pig dies two blocks away, on the stone floor.
        var pig = new Pig { Level = world, Position = new VectorD(10.5, 64, 8.5) };
        pig.InitializeAi(false);
        await pig.KillAsync(pig);

        // The catalyst took its experience, which the pig won't drop, and bloomed.
        Assert.True(pig.ExperienceConsumed);
        Assert.Equal("true", world.BlockUpdates.GetBlock(catalystPosition).GetProperty("bloom"));
        var cursor = Assert.Single(catalyst.Charge.Cursors);
        Assert.Equal(new Vector(10, 64, 8), cursor.Position);
        Assert.InRange(cursor.Charge, 1, 3);

        // On its first tick the charge turns the stone where the pig died into sculk.
        catalyst.Tick(world);
        Assert.Equal(BlockIds.Sculk, world.BlockUpdates.GetBlock(new Vector(10, 63, 8)).RegistryId);
    }

    // A complete chunk with a stone floor under y 64.
    private static void AddFloor(WorldData.World world)
    {
        var chunk = new Chunk(0, 0);
        for (var x = 0; x < 16; x++)
        {
            for (var z = 0; z < 16; z++)
                chunk.SetBlock(x, 63, z, BlocksRegistry.Stone);
        }

        chunk.SetChunkStatus(ChunkGenStage.full);
        world.LoadRegionByChunk(0, 0).SetChunk(chunk);
    }
}
