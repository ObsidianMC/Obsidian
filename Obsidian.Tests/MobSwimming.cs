using Obsidian.API;
using Obsidian.API.AI;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class MobSwimming
{
    [Fact]
    public void PathNodeTypesMapBothWaysToTheGeneratedServerTypes()
    {
        var names = Enum.GetValues<PathType>().Length;
        Assert.Equal(names, Enum.GetValues<PathNodeType>().Length);
        foreach (var type in Enum.GetValues<PathNodeType>())
            Assert.Equal(type, PathNodeMapping.ToPublic(PathNodeMapping.ToInternal(type)));
    }

    [Theory]
    [InlineData(EntityType.Chicken, 4.5, 65, 8.5, 11.5, 66, 8.5)]
    [InlineData(EntityType.Pig, 4.5, 65, 8.5, 11.5, 66, 8.5)]
    [InlineData(EntityType.Pig, 4.5, 65, 4.5, 11.5, 66, 11.5)]
    [InlineData(EntityType.Pig, 4.5, 65, 12.5, 11.5, 66, 3.5)]
    [InlineData(EntityType.Pig, 9.5, 66, 8.5, 4.5, 65, 8.5)]
    [InlineData(EntityType.Cow, 4.5, 65, 8.5, 11.5, 66, 8.5)]
    [InlineData(EntityType.Sheep, 4.5, 65, 8.5, 11.5, 66, 3.5)]
    public async Task GroundMobNavigatesAcrossOneBlockTerrace(EntityType type, double sx, double sy, double sz, double tx, double ty, double tz)
    {
        await using var context = TestServerFactory.CreateServer($"terrace-{Guid.NewGuid():N}");
        var chunk = new Chunk(0, 0, -64, 384, ChunkGenStage.full);
        chunk.SetChunkStatus(ChunkGenStage.full);
        for (var x = 2; x <= 13; x++)
        {
            for (var z = 2; z <= 13; z++)
            {
                chunk.SetBlock(x, 64, z, BlocksRegistry.Stone);
                if (x >= 8)
                    chunk.SetBlock(x, 65, z, BlocksRegistry.Stone);
            }
        }

        context.World.LoadRegionByChunk(0, 0).SetChunk(chunk);
        var mob = Obsidian.Entities.Factories.EntitySpawner.CreateMob(context.World, type)!;
        mob.Position = new VectorD(sx, sy, sz);
        mob.InitializeAi(false);
        var goals = Assert.IsType<GoalSelector>(mob.GoalController);
        goals.Clear();
        goals.AddGoal(0, new FloatGoal((PathfinderMob)mob));
        Assert.IsType<Navigator>(mob.Navigator).NavigateTo(new VectorD(tx, ty, tz));

        for (var tick = 0; tick < 400 && (mob.Position - new VectorD(tx, ty, tz)).MagnitudeSquared() > 0.25f; tick++)
            await mob.TickAsync();

        Assert.True((mob.Position - new VectorD(tx, ty, tz)).MagnitudeSquared() <= 1f, $"{type} stuck at {mob.Position}, target {tx},{ty},{tz}");
    }
}
