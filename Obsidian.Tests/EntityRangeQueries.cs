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
        context.World.LoadRegionByChunk(-64, 64).Entities.TryAdd(entity.EntityId, entity);

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
        var region = context.World.LoadRegionByChunk(32, 0);
        region.Entities.TryAdd(near.EntityId, near);
        region.Entities.TryAdd(far.EntityId, far);

        Assert.Same(near, Assert.Single(context.World.GetNonPlayerEntitiesInRange(origin, distance)));
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
