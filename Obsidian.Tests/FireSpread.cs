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
/// Fire (milestone 4 of the block ticking plan): the owner class generated from vanilla's FireBlock, its flammability
/// odds, its placement state, and its scheduled tick (burn-out and spread).
/// </summary>
public sealed class FireSpread
{
    private const int Ground = 69;

    private static readonly Vector Base = new(8, Ground + 1, 8);

    [Fact(DisplayName = "Fire's tick and updateShape are owned by vanilla's FireBlock")]
    public void OwnersAreGenerated()
    {
        Assert.Same(TickBehaviors.FireBlock, BlocksRegistry.GetTickBehavior(BlockIds.Fire));
        Assert.Same(ShapeUpdateBehaviors.FireBlock, BlocksRegistry.GetShapeUpdateBehavior(BlockIds.Fire));
    }

    [Fact(DisplayName = "Flammable blocks carry vanilla's FireBlock.bootStrap odds")]
    public void FlammabilityIsGenerated()
    {
        Assert.Equal((5, 5), BlocksRegistry.GetFireOdds(BlockIds.OakLog));
        Assert.Equal((5, 20), BlocksRegistry.GetFireOdds(BlockIds.OakPlanks));
        Assert.Equal((60, 100), BlocksRegistry.GetFireOdds(BlockIds.ShortGrass));
        Assert.Null(BlocksRegistry.GetFireOdds(BlockIds.Stone));
    }

    [Fact(DisplayName = "Fire can be placed beside a log on air, and is placed with a face toward it", Timeout = TestServerFactory.TestTimeout)]
    public async Task FireTakesAFaceTowardAFlammableLog()
    {
        await using var context = TestServerFactory.CreateServer($"fire-face-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);

        Held(world).SetBlock(Base + Vector.East, BlocksRegistry.GetDefault(BlockIds.OakLog));
        // Vanilla's FireBlock.b: a fire on a sturdy floor has no faces, so the floor is cleared.
        Held(world).SetBlock(Base + Vector.Down, BlocksRegistry.Air);
        Assert.True(FireBlockBehavior.CanBePlaced(world.BlockUpdates, Base));

        var fire = FireBlockBehavior.StateFor(world.BlockUpdates, Base);
        Assert.Equal("true", fire.GetProperty("east"));
        Assert.Equal("false", fire.GetProperty("up"));
        Assert.Equal("false", fire.GetProperty("north"));
        await world.DoWorldTickAsync();
    }

    [Fact(DisplayName = "Fire can't be placed in mid-air with nothing to burn or stand on", Timeout = TestServerFactory.TestTimeout)]
    public async Task FireNeedsASupport()
    {
        await using var context = TestServerFactory.CreateServer($"fire-support-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);

        var high = Base + new Vector(0, 5, 0);
        Assert.False(FireBlockBehavior.CanBePlaced(world.BlockUpdates, high));
        await world.DoWorldTickAsync();
    }

    [Fact(DisplayName = "Fire on a sturdy floor with nothing burnable beside it goes out after age 3", Timeout = TestServerFactory.TestTimeout)]
    public async Task FireBurnsOutOverOldAge()
    {
        await using var context = TestServerFactory.CreateServer($"fire-burnout-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);
        world.LevelData.GameRules["fire_spread_radius_around_player"] = "-1";

        Held(world).SetBlock(Base, BlocksRegistry.Fire.WithProperty("age", "4"));
        TickBehaviors.FireBlock.Tick(world.BlockUpdates, Base, Held(world).GetBlock(Base), new LegacyRandomSource(1));

        Assert.True(Held(world).GetBlock(Base).IsAir);
        await world.DoWorldTickAsync();
    }

    [Fact(DisplayName = "Young fire on a sturdy floor stays lit on its tick", Timeout = TestServerFactory.TestTimeout)]
    public async Task YoungFireStaysLit()
    {
        await using var context = TestServerFactory.CreateServer($"fire-young-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);

        Held(world).SetBlock(Base, BlocksRegistry.Fire.WithProperty("age", "0"));
        TickBehaviors.FireBlock.Tick(world.BlockUpdates, Base, Held(world).GetBlock(Base), new LegacyRandomSource(1));

        Assert.Equal(BlockIds.Fire, Held(world).GetBlock(Base).RegistryId);
        await world.DoWorldTickAsync();
    }

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

    [Fact(DisplayName = "Fire whose floor is gone and has nothing burnable beside it updates to air", Timeout = TestServerFactory.TestTimeout)]
    public async Task FireGoesOutWhenItCantSurvive()
    {
        await using var context = TestServerFactory.CreateServer($"fire-shape-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);

        Held(world).SetBlock(Base + Vector.Down, BlocksRegistry.Air);
        var updated = ShapeUpdateBehaviors.FireBlock.UpdateShape(
            world.BlockUpdates, Base, BlocksRegistry.Fire, BlockFace.Down, Base + Vector.Down, BlocksRegistry.Air);

        Assert.NotNull(updated);
        Assert.True(updated!.IsAir);
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
