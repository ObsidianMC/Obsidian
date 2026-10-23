using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class EntityRangeQueries
{
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task UnboundedQueryFindsLoadedEntitiesAndMissingOwners()
    {
        await using var context = TestServerFactory.CreateServer($"entity-range-{Guid.NewGuid():N}");
        var entity = new Entity { EntityId = 1, Level = context.World, Position = new VectorD(-1024, 64, 1024) };
        context.World.LoadRegionByChunk(-64, 64);
        Assert.True(context.World.TryAddEntity(entity));

        Assert.Same(entity, Assert.Single(context.World.GetEntitiesInRange(VectorD.Zero, float.MaxValue)));
        var missingOwner = Guid.NewGuid();
        Assert.Null(context.World.GetEntitiesInRange(VectorD.Zero, float.MaxValue).FirstOrDefault(candidate => candidate.Uuid == missingOwner));
    }

    [Theory]
    [InlineData(32f)]
    [InlineData(512f)]
    public async Task QueryPreservesDistanceFilteringAcrossRegionBoundaries(float distance)
    {
        await using var context = TestServerFactory.CreateServer($"entity-range-{Guid.NewGuid():N}");
        var origin = new VectorD(511, 64, 0);
        var near = new Entity { EntityId = 1, Level = context.World, Position = new VectorD(512, 64, 0) };
        var far = new Entity { EntityId = 2, Level = context.World, Position = new VectorD(512, 64 + distance + 1, 0) };
        context.World.LoadRegionByChunk(32, 0);
        Assert.True(context.World.TryAddEntity(near));
        Assert.True(context.World.TryAddEntity(far));

        Assert.Same(near, Assert.Single(context.World.GetNonPlayerEntitiesInRange(origin, distance)));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task QueriesFollowEntitiesBetweenChunksAndOutOfTheLevel()
    {
        await using var context = TestServerFactory.CreateServer($"entity-range-{Guid.NewGuid():N}");
        var world = context.World;
        var entity = new Entity { EntityId = 1, Level = world, Position = new VectorD(8, 64, 8) };
        Assert.True(world.TryAddEntity(entity));

        // Moved three chunks over without the level hearing of it, as entities move.
        entity.Position = new VectorD(56, 64, 8);
        Assert.Empty(world.GetNonPlayerEntitiesInRange(new VectorD(8, 64, 8), 4));
        Assert.Same(entity, Assert.Single(world.GetNonPlayerEntitiesInRange(new VectorD(55, 64, 8), 4)));

        await world.DestroyEntityAsync(entity);
        Assert.Empty(world.GetNonPlayerEntitiesInRange(new VectorD(55, 64, 8), 4));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task UnloadingARegionTakesItsUnsavedEntitiesOutOfQueries()
    {
        await using var context = TestServerFactory.CreateServer($"entity-range-{Guid.NewGuid():N}");
        var world = context.World;
        // In a chunk never generated, so the region's final save doesn't take it.
        var entity = new Entity { EntityId = 1, Level = world, Position = new VectorD(8, 64, 8) };
        Assert.True(world.TryAddEntity(entity));

        await world.UnloadRegionAsync(0, 0);

        Assert.Empty(world.GetNonPlayerEntitiesInRange(new VectorD(8, 64, 8), 4));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task LevelFindsEntitiesByUuidAndRefusesDuplicates()
    {
        await using var context = TestServerFactory.CreateServer($"entity-uuid-{Guid.NewGuid():N}");
        var world = context.World;
        var uuid = Guid.NewGuid();
        var entity = new Entity { EntityId = 1, Level = world, Position = new VectorD(8, 64, 8), Uuid = uuid };
        Assert.True(world.TryAddEntity(entity));

        Assert.Same(entity, world.FindEntity(uuid));
        Assert.Same(entity, world.FindEntityInRange(uuid, new VectorD(8, 64, 12), 5));
        Assert.Null(world.FindEntityInRange(uuid, new VectorD(8, 64, 20), 5));

        // Like vanilla's, a level holds one entity per UUID.
        var duplicate = new Entity { EntityId = 2, Level = world, Position = new VectorD(9, 64, 9), Uuid = uuid };
        Assert.False(world.TryAddEntity(duplicate));
        Assert.Same(entity, world.FindEntity(uuid));

        await world.DestroyEntityAsync(entity);
        Assert.Null(world.FindEntity(uuid));
        Assert.True(world.TryAddEntity(duplicate));
        Assert.Same(duplicate, world.FindEntity(uuid));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task EntityUuidIsSetOnce()
    {
        await using var context = TestServerFactory.CreateServer($"entity-uuid-{Guid.NewGuid():N}");
        var uuid = Guid.NewGuid();
        var entity = new Entity { Level = context.World, Uuid = uuid };

        // Setting the same UUID again changes nothing; another one throws.
        entity.Uuid = uuid;
        Assert.Throws<InvalidOperationException>(() => entity.Uuid = Guid.NewGuid());
        Assert.Equal(uuid, entity.Uuid);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task RestoredArrowWithAbsentOwnerCompletesItsTick()
    {
        await using var context = TestServerFactory.CreateServer($"arrow-owner-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Observer");
        Assert.True(context.World.TryAddPlayer(player));
        context.World.GetRegionForChunk(0, 0)!.SetChunk(new Chunk(0, 0, -64, 384, ChunkGenStage.full));
        var owner = new Entity { Level = context.World, Uuid = Guid.NewGuid() };
        var original = new Arrow { Level = context.World, Type = EntityType.Arrow, Owner = owner, Position = new VectorD(8, 70, 8) };
        var arrow = Assert.IsType<Arrow>(EntityNbt.Load(EntityNbt.Save(original)!, context.World));

        await arrow.TickAsync();

        Assert.Null(arrow.Owner);
        Assert.True(arrow.Motion.Y < 0);
    }
}
