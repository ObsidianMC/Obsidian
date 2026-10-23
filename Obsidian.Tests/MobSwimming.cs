using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Obsidian.WorldData.Structures;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class MobSwimming
{
    [Theory]
    [InlineData(EntityType.Pig, true)]
    [InlineData(EntityType.Cod, false)]
    public async Task FlowingWaterPushesOnlyMobsAffectedByCurrents(EntityType type, bool pushed)
    {
        await using var context = TestServerFactory.CreateServer($"mob-current-{Guid.NewGuid():N}");
        var chunk = CreatePool();
        chunk.SetBlock(8, 68, 8, BlocksRegistry.Water);
        chunk.SetBlock(9, 68, 8, BlockStateParser.TryParse("minecraft:water[level=7]")!);
        chunk.SetBlock(7, 68, 8, BlocksRegistry.Stone);
        chunk.SetBlock(8, 68, 7, BlocksRegistry.Stone);
        chunk.SetBlock(8, 68, 9, BlocksRegistry.Stone);
        context.World.LoadRegionByChunk(0, 0).SetChunk(chunk);
        context.World.Fluids.Track(chunk);
        var mob = Obsidian.Entities.Factories.EntitySpawner.CreateMob(context.World, type)!;
        mob.Position = new VectorD(8.5, 68.2, 8.5);
        mob.InitializeAi(false);

        EntityMovement.Move(mob);

        Assert.Equal(pushed, mob.Motion.X > 0);
        Assert.Equal(0, mob.Motion.Z);
    }

    [Fact]
    public async Task ChickenCanNavigateOutOfWaterOntoABank()
    {
        await using var context = TestServerFactory.CreateServer($"chicken-shore-{Guid.NewGuid():N}");
        var chunk = CreatePool();
        for (var x = 8; x <= 13; x++)
            for (var z = 2; z <= 13; z++)
                chunk.SetBlock(x, 66, z, BlocksRegistry.Stone);
        context.World.LoadRegionByChunk(0, 0).SetChunk(chunk);
        var chicken = new Chicken
        {
            EntityId = 1,
            Level = context.World,
            Position = new VectorD(6.5, 66.7, 8.5)
        };
        chicken.InitializeAi(false);
        var goals = Assert.IsType<GoalSelector>(chicken.GoalController);
        goals.Clear();
        goals.AddGoal(0, new FloatGoal(chicken));
        Assert.IsType<Navigator>(chicken.Navigator).NavigateTo(new VectorD(11.5, 67, 8.5));

        for (var tick = 0; tick < 400 && chicken.Position.X < 9; tick++)
            await chicken.TickAsync();

        Assert.True(chicken.Position.X >= 9, $"Chicken stuck at {chicken.Position}");
        Assert.True(chicken.Position.Y >= 67);
        Assert.False(chicken.InWater);
    }

    [Fact]
    public async Task LandAndAquaticMobsNavigateWater()
    {
        {
            await using var context = TestServerFactory.CreateServer($"mob-swim-{Guid.NewGuid():N}");
            var chunk = CreatePool();
            context.World.LoadRegionByChunk(0, 0).SetChunk(chunk);
            var pig = new Pig
            {
                Level = context.World,
                Position = new VectorD(4.5, 65, 8.5)
            };
            pig.InitializeAi(false);

            var path = new PathFinder(pig).FindPath(new VectorD(11.5, 67, 8.5));

            Assert.NotNull(path);
            Assert.True(path.ReachedTarget);
            Assert.All(path.Nodes, node => Assert.Equal(67, node.Y));

            var navigator = Assert.IsType<Navigator>(pig.Navigator);
            navigator.NavigateTo(new VectorD(11.5, 67, 8.5));
            navigator.Tick();
            pig.Position += new VectorD(0, 0.8, 0);
            navigator.Tick();
            pig.MoveControl.Tick();
            Assert.True(pig.MoveControl.Acceleration.X > 0);
        }

        {
            await using var context = TestServerFactory.CreateServer($"mob-swim-{Guid.NewGuid():N}");
            var chunk = CreatePool();
            context.World.LoadRegionByChunk(0, 0).SetChunk(chunk);
            var fish = new Cod
            {
                Level = context.World,
                Position = new VectorD(4.5, 65, 8.5)
            };
            fish.InitializeAi(false);
            fish.MoveControl.MoveTo(new VectorD(10.5, 66, 8.5), 1);
            fish.MoveControl.Tick();
            Assert.Equal(fish.MovementSpeed * 0.1f / Math.Sqrt(37), fish.MoveControl.Acceleration.Y, 6);

            fish.Position = new VectorD(4.5, 70, 8.5);
            fish.MoveControl.Tick();
            Assert.Equal(VectorD.Zero, fish.MoveControl.Acceleration);
        }
    }

    private static Chunk CreatePool()
    {
        var chunk = new Chunk(0, 0, -64, 384, ChunkGenStage.full);
        chunk.SetChunkStatus(ChunkGenStage.full);
        for (var x = 2; x <= 13; x++)
            for (var z = 2; z <= 13; z++)
                for (var y = 60; y <= 66; y++)
                    chunk.SetBlock(x, y, z, BlocksRegistry.Water);
        return chunk;
    }
}
