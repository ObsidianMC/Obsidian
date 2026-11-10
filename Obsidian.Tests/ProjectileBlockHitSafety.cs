using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.Entities;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using ServerPlayer = Obsidian.Entities.Player;

namespace Obsidian.Tests;

/// <summary>
/// What vanilla can't check for the projectile hit path (<see cref="ProjectileBlockHits"/>): the level's tick never
/// waits for the owner to grant a criterion, and a hit whose projectile or block is gone does nothing.
/// </summary>
[Collection(nameof(PluginTagFiles))]
public sealed class ProjectileBlockHitSafety
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static readonly Vector target = new(0, 64, 0);

    [Fact]
    public async Task TargetHitInTheLevelTickDoesNotWaitForTheOwner()
    {
        await using var context = await CreateWorldAsync();
        var world = context.World;
        var owner = OwnerOutsideTheLevel(context);
        world.BlockUpdates.SetBlock(target, BlocksRegistry.Get("minecraft:target"));

        var previous = AdvancementsRegistry.Current.Values.ToArray();
        using var definition = JsonDocument.Parse("""{"criteria":{"hit":{"trigger":"minecraft:target_hit"}}}""");
        AdvancementsRegistry.Apply(previous.Append(AdvancementsRegistry.Parse("probe:target", definition.RootElement)));
        try
        {
            SpawnArrow(world, owner, falling: true);

            // The owner's tick gate is held, as by their own tick or a save under way.
            var release = new TaskCompletionSource();
            var held = await HoldGateAsync(owner, release.Task);

            // The level's tick lands the hit without waiting for the owner.
            await world.DoWorldTickAsync().WaitAsync(Wait);
            Assert.Equal("15", world.BlockUpdates.GetBlock(target).GetProperty("power"));
            Assert.False(owner.Advancements.HasCriterion("probe:target", "hit"));

            // The criterion is granted in the background once the gate is let go.
            release.SetResult();
            await held.WaitAsync(Wait);
            await context.Server.BackgroundWork.DrainAsync().WaitAsync(Wait);
            Assert.True(owner.Advancements.HasCriterion("probe:target", "hit"));
        }
        finally
        {
            AdvancementsRegistry.Apply(previous);
        }
    }

    [Fact]
    public async Task HitOfARemovedProjectileOrAnUnloadedBlockDoesNothing()
    {
        await using var context = await CreateWorldAsync();
        var world = context.World;
        var owner = OwnerOutsideTheLevel(context);
        var targetBlock = BlocksRegistry.Get("minecraft:target");
        var unloaded = new Vector(16, 64, 0);
        world.BlockUpdates.SetBlock(target, targetBlock);
        world.BlockUpdates.SetBlock(unloaded, targetBlock);

        // A projectile removed before its hit lands (by a plugin, say) hits nothing.
        var removed = SpawnArrow(world, owner, falling: false);
        Assert.True(await world.DestroyEntityAsync(removed));
        await ProjectileBlockHits.HitAsync(world, removed, owner, TopHit(target));
        Assert.Equal("0", world.BlockUpdates.GetBlock(target).GetProperty("power"));

        // A hit whose block's chunk unloaded in the meantime leaves it alone, and doesn't load it again.
        var region = Assert.IsType<Region>(world.LoadRegionByChunk(1, 0));
        await region.FlushAsync();
        await region.UnloadChunksAsync([(1, 0)]);
        var live = SpawnArrow(world, owner, falling: false);
        await ProjectileBlockHits.HitAsync(world, live, owner, TopHit(unloaded));
        Assert.Null(world.GetLoadedChunk(1, 0));
    }

    // A world of tracked empty chunks around the target, with a player near it so that it simulates.
    private static async Task<RealServerContext> CreateWorldAsync()
    {
        var context = TestServerFactory.CreateServer($"projectile-hit-safety-{Guid.NewGuid():N}");
        var world = context.World;
        for (var chunkX = -2; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -2; chunkZ <= 1; chunkZ++)
            {
                var chunk = WorldBlockEntities.AddEmptyChunk(world, chunkX, chunkZ);
                world.Fluids.Track(chunk);
                await world.GetBlockAsync(new Vector(chunkX * 16, 64, chunkZ * 16));
            }
        }

        var watcher = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Watcher",
            (VectorD)target + new VectorD(9.5, 0, 0.5));
        world.Players.TryAdd(watcher.Uuid, watcher);
        return context;
    }

    // The arrows' owner: a player the level doesn't tick, so that the world's tick never waits for their gate itself.
    private static ServerPlayer OwnerOutsideTheLevel(RealServerContext context) =>
        TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Owner",
            (VectorD)target + new VectorD(0.5, 0, 9.5));

    private static Arrow SpawnArrow(WorldData.World world, ServerPlayer owner, bool falling)
    {
        var arrow = new Arrow
        {
            Level = world,
            Type = EntityType.Arrow,
            Owner = owner,
            EntityId = Server.GetNextEntityId(),
            Position = (VectorD)target + new VectorD(0.5, 1.8, 0.5),
            Motion = falling ? new VectorD(0, -1.5, 0) : VectorD.Zero
        };
        Assert.True(world.TrySpawnEntity(arrow, hostOwned: true));
        return arrow;
    }

    private static ProjectileBlockHit TopHit(Vector block) =>
        new(block, BlockFace.Up, (VectorD)block + new VectorD(0.5, 1, 0.5), 0.5);

    // Holds the player's tick gate until released, as a tick or grant under way does. Returns once it's held, with what
    // lets go of it.
    private static async Task<Task> HoldGateAsync(PlayerBase player, Task release)
    {
        var holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = Task.Run(() => player.GrantAdvancementAsync(async () =>
        {
            holding.TrySetResult();
            await release;
            return false;
        }).AsTask());

        await holding.Task.WaitAsync(Wait);
        return held;
    }
}
