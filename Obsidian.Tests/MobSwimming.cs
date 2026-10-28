using Obsidian.API;
using Obsidian.API.AI;
using Obsidian.API.World;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Obsidian.WorldData.Structures;
using System;
using System.Linq;
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

    [Theory]
    [InlineData(EntityType.Chicken, true)]
    [InlineData(EntityType.Turtle, false)]
    public async Task FloatGoalOwnsNavigatorCanFloat(EntityType type, bool canFloat)
    {
        await using var context = TestServerFactory.CreateServer($"can-float-{Guid.NewGuid():N}");
        context.World.LoadRegionByChunk(0, 0).SetChunk(CreatePool());
        var mob = Obsidian.Entities.Factories.EntitySpawner.CreateMob(context.World, type)!;
        mob.Position = new VectorD(6.5, 66.7, 8.5);
        mob.InitializeAi(false);

        Assert.Equal(canFloat, Assert.IsType<Navigator>(mob.Navigator).CanFloat);
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
    public async Task PluginPathfinderReturnsPathWithoutStartingNavigation()
    {
        await using var context = TestServerFactory.CreateServer($"plugin-path-{Guid.NewGuid():N}");
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
        var navigator = Assert.IsType<Navigator>(chicken.Navigator);
        var pathfinder = Assert.IsAssignableFrom<IPathfinder>(navigator);

        var path = pathfinder.FindPath(new VectorD(11.5, 67, 8.5));

        Assert.NotNull(path);
        Assert.True(path.ReachedTarget);
        Assert.Equal(new VectorD(11.5, 67, 8.5), path.Nodes[^1]);
        Assert.False(navigator.IsNavigating);
        Assert.Throws<ArgumentOutOfRangeException>(() => pathfinder.FindPath(new VectorD(double.NaN, 67, 8.5)));
    }

    [Fact]
    public void PathNodeTypesMapBothWaysToTheGeneratedServerTypes()
    {
        var names = Enum.GetValues<PathType>().Length;
        Assert.Equal(names, Enum.GetValues<PathNodeType>().Length);
        foreach (var type in Enum.GetValues<PathNodeType>())
            Assert.Equal(type, PathNodeMapping.ToPublic(PathNodeMapping.ToInternal(type)));
    }

    // A body that is not a mob: the search reads only what IPathBody exposes, so a plugin body gets the same path.
    private sealed class ChickenBody(Chicken chicken, Navigator navigator) : IPathBody
    {
        public VectorD Position => chicken.Position;
        public EntityDimension Dimension => chicken.Dimension;
        public ILevel Level => chicken.Level;
        public IPathTerrain Terrain => chicken.Terrain;
        public float StepHeight => chicken.GetAttributeValue("minecraft:generic.step_height");
        public int MaxFall => 3;
        public float FollowRange => 16;
        public bool CanOpenDoors => navigator.CanOpenDoors;
        public bool CanFloat => navigator.CanFloat;
        public bool SwimmingNavigation => false;
        public bool FlyingNavigation => false;
        public bool InWater => chicken.InWater;
        public float Malus(PathNodeType type) => chicken.GetPathfindingMalus(PathNodeMapping.ToInternal(type));
        public float NodeCost(Vector feetPosition, IBlock feet, IBlock floor) => chicken.PathBody.NodeCost(feetPosition, feet, floor);
    }

    [Fact]
    public async Task PathSearchForPluginBodyMatchesTheMobsPath()
    {
        await using var context = TestServerFactory.CreateServer($"path-body-{Guid.NewGuid():N}");
        var chunk = CreatePool();
        for (var x = 8; x <= 13; x++)
            for (var z = 2; z <= 13; z++)
                chunk.SetBlock(x, 66, z, BlocksRegistry.Stone);
        context.World.LoadRegionByChunk(0, 0).SetChunk(chunk);
        var chicken = new Chicken { EntityId = 1, Level = context.World, Position = new VectorD(6.5, 66.7, 8.5) };
        chicken.InitializeAi(false);
        var navigator = Assert.IsType<Navigator>(chicken.Navigator);
        var target = new VectorD(11.5, 67, 8.5);

        var fromMob = Assert.IsAssignableFrom<IPathfinder>(navigator).FindPath(target);
        var fromBody = PathSearch.For(new ChickenBody(chicken, navigator)).FindPath(target);

        Assert.NotNull(fromMob);
        Assert.NotNull(fromBody);
        Assert.Equal(fromMob.Nodes, fromBody.Nodes);
        Assert.Equal(fromMob.ReachedTarget, fromBody.ReachedTarget);
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

            var path = new PathFinder(pig.PathBody).FindPath(new VectorD(11.5, 67, 8.5));

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
            for (var z = 2; z <= 13; z++)
            {
                chunk.SetBlock(x, 64, z, BlocksRegistry.Stone);
                if (x >= 8)
                    chunk.SetBlock(x, 65, z, BlocksRegistry.Stone);
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

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task NarrowMobStepUpNeedsHeadClearance(bool ceiling, bool expectLedge)
    {
        // A chicken (narrower than one block) stepping onto a one-block ledge needs the cell above its start to be clear.
        var chunk = new Chunk(0, 0, -64, 384, ChunkGenStage.full);
        chunk.SetChunkStatus(ChunkGenStage.full);
        for (var x = 2; x <= 13; x++)
            for (var z = 2; z <= 13; z++)
            {
                chunk.SetBlock(x, 64, z, BlocksRegistry.Stone);
                if (x >= 8)
                    chunk.SetBlock(x, 65, z, BlocksRegistry.Stone);
            }
        if (ceiling)
            chunk.SetBlock(8, 66, 8, BlocksRegistry.Stone);
        await using var context = TestServerFactory.CreateServer($"headroom-{Guid.NewGuid():N}");
        context.World.LoadRegionByChunk(0, 0).SetChunk(chunk);
        var mob = Obsidian.Entities.Factories.EntitySpawner.CreateMob(context.World, EntityType.Chicken)!;
        mob.Position = new VectorD(7.5, 65, 8.5);
        mob.InitializeAi(false);

        var neighbors = new WalkNodeEvaluator(((PathfinderMob)mob).PathBody).GetNeighbors(new VectorD(7.5, 65, 8.5)).ToList();
        Assert.Equal(expectLedge, neighbors.Any(node => node.X == 8.5 && node.Y == 66 && node.Z == 8.5));
    }

    [Fact]
    public void GeneratedPathTypeMalusMatchesVanillaDefaults()
    {
        // Vanilla PathType defaults (fls): negative rejects a node; water costs 8, damage fire 16, breach 4.
        Assert.Equal(8f, Obsidian.Entities.AI.PathTypeMalus.Get(Obsidian.Entities.AI.PathType.WATER));
        Assert.Equal(16f, Obsidian.Entities.AI.PathTypeMalus.Get(Obsidian.Entities.AI.PathType.FIRE));
        Assert.Equal(4f, Obsidian.Entities.AI.PathTypeMalus.Get(Obsidian.Entities.AI.PathType.BREACH));
        Assert.Equal(-1f, Obsidian.Entities.AI.PathTypeMalus.Get(Obsidian.Entities.AI.PathType.LAVA));
        Assert.Equal(0f, Obsidian.Entities.AI.PathTypeMalus.Get(Obsidian.Entities.AI.PathType.OPEN));
    }

    [Theory]
    [InlineData(EntityType.Goat)]
    [InlineData(EntityType.Sniffer)]
    [InlineData(EntityType.Armadillo)]
    [InlineData(EntityType.Warden)]
    public async Task VanillaCanFloatSpeciesEnableNavigatorCanFloat(EntityType type)
    {
        // Vanilla constructors of Goat, Sniffer, Armadillo and Warden call setCanFloat(true).
        await using var context = TestServerFactory.CreateServer($"canfloat-{Guid.NewGuid():N}");
        var mob = Obsidian.Entities.Factories.EntitySpawner.CreateMob(context.World, type)!;
        mob.InitializeAi(false);
        Assert.True(Assert.IsType<Navigator>(mob.Navigator).CanFloat);
    }

    [Fact]
    public async Task SpeciesPathfindingMalusOverridesMatchVanillaConstructors()
    {
        await using var context = TestServerFactory.CreateServer($"malus-{Guid.NewGuid():N}");
        // Vanilla Animal sets FIRE_IN_NEIGHBOR 16 and FIRE -1; Wolf sets POWDER_SNOW and ON_TOP_OF_POWDER_SNOW -1.
        // Other entries, including all of Horse's since 26.1, keep the generated defaults.
        var cow = Assert.IsAssignableFrom<PathfinderMob>(new Cow { Level = context.World });
        Assert.Equal(16f, cow.GetPathfindingMalus(PathType.FIRE_IN_NEIGHBOR));
        Assert.Equal(-1f, cow.GetPathfindingMalus(PathType.FIRE));
        Assert.Equal(PathTypeMalus.Get(PathType.WATER), cow.GetPathfindingMalus(PathType.WATER));

        var horse = new Horse { Level = context.World };
        Assert.Equal(PathTypeMalus.Get(PathType.DAMAGING_IN_NEIGHBOR), horse.GetPathfindingMalus(PathType.DAMAGING_IN_NEIGHBOR));

        var wolf = new Wolf { Level = context.World };
        Assert.Equal(-1f, wolf.GetPathfindingMalus(PathType.POWDER_SNOW));
        Assert.Equal(-1f, wolf.GetPathfindingMalus(PathType.ON_TOP_OF_POWDER_SNOW));
    }

    [Fact]
    public async Task OpenDoorDoesNotBlockMobsWithoutDoorOpening()
    {
        // Vanilla's DOOR_OPEN block rule reads canOpenDoors, which defaults to true, so a cow can walk through an open door.
        await using var context = TestServerFactory.CreateServer($"opendoor-{Guid.NewGuid():N}");
        var chunk = CreatePool();
        chunk.SetBlock(8, 66, 8, BlockStateParser.TryParse("minecraft:oak_door[facing=north,half=lower,hinge=left,open=true,powered=false]")!);
        context.World.LoadRegionByChunk(0, 0).SetChunk(chunk);

        var cow = new Cow { Level = context.World };
        cow.InitializeAi(false);
        var node = PathTypeClassifier.ForNode(cow.PathBody, new Vector(8, 66, 8));
        Assert.NotEqual(PathType.BLOCKED, node);
        Assert.True(cow.GetPathfindingMalus(node) >= 0);
    }

    [Fact]
    public void PathTypeOfBlockStatesMatchesVanillaWalkNodeEvaluator()
    {
        // Vanilla WalkNodeEvaluator.getPathTypeFromState: solid is BLOCKED, water WATER, a bottom slab BLOCKED (SlabBlock
        // does not override isPathfindable for LAND), and an open flower or air is OPEN.
        Assert.Equal(PathType.BLOCKED, PathTypeClassifier.Base(BlocksRegistry.Stone.GetHashCode()));
        Assert.Equal(PathType.WATER, PathTypeClassifier.Base(BlocksRegistry.Water.GetHashCode()));
        Assert.Equal(PathType.BLOCKED, PathTypeClassifier.Base(Obsidian.WorldData.Structures.BlockStateParser.TryParse("minecraft:stone_slab[type=bottom]")!.GetHashCode()));
        Assert.Equal(PathType.OPEN, PathTypeClassifier.Base(BlocksRegistry.Air.GetHashCode()));
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
