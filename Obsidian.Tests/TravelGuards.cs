using Obsidian.API;
using Obsidian.API.Entities;
using Obsidian.API.Plugins;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Plugins;
using Obsidian.Plugins.Archives;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData.Pistons;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Work that awaits between finding an entity and changing it must not change it once it has travelled to another
/// level meanwhile. Each test completes the travel at the exact point between the two, held there by a gate.
/// </summary>
public sealed class TravelGuards
{
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task DamageSkipsMobTransferredWhileAPluginHookAwaits()
    {
        await using var source = TestServerFactory.CreateServer($"damage-travel-source-{Guid.NewGuid():N}");
        await using var destination = TestServerFactory.CreateServer($"damage-travel-destination-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(source.World);
        WorldBlockEntities.AddEmptyChunk(destination.World);
        var hook = new GatedHurtBehavior();
        var mob = SpawnPluginMob(source.World, hook);
        var attacker = Spawn(source.World, new VectorD(10.5, 70, 8.5));
        var health = mob.Health;

        // The hit waits in the plugin's hook while the mob travels, to the same coordinates, so rejecting it takes
        // checking the level the mob is in.
        var hit = mob.DamageCombatAsync(attacker, 6, CombatDamageKind.Melee).AsTask();
        await hook.Reached.Task;
        await mob.TeleportAsync(destination.World, mob.Position);
        Assert.Same(destination.World, mob.Level);
        hook.Release.SetResult();
        await hit;

        // No health, knockback or hurt state.
        Assert.Equal(health, mob.Health);
        Assert.Equal(VectorD.Zero, mob.Motion);
        Assert.Equal(0, mob.AcceptedDamageCount);
        Assert.Null(mob.LastAttacker);

        // In its new level, a hit from there lands, knockback and all.
        hook.Open = true;
        var local = Spawn(destination.World, new VectorD(10.5, 70, 8.5));
        await mob.DamageCombatAsync(local, 6, CombatDamageKind.Melee);
        Assert.Equal(health - 6, mob.Health);
        Assert.NotEqual(VectorD.Zero, mob.Motion);
        Assert.Same(local, mob.LastAttacker);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PistonSkipsEntityTransferredBeforePushCommit()
    {
        await using var source = TestServerFactory.CreateServer($"piston-travel-source-{Guid.NewGuid():N}");
        await using var destination = TestServerFactory.CreateServer($"piston-travel-destination-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(source.World);
        WorldBlockEntities.AddEmptyChunk(destination.World);
        var piston = new MovingPistonBlockEntity { BlockPosition = new Vector(8, 70, 8) };
        piston.Configure(source.World, BlocksRegistry.Stone, BlockFace.East, extending: true, source: false);
        var zombie = Spawn(source.World, new VectorD(8.6, 70, 8.5));

        // The first half of the move pushes the zombie in its path.
        var beforePush = zombie.Position;
        await piston.TickAsync();
        Assert.True(zombie.Position.X > beforePush.X);

        // The second half finds it again, and it travels, to the same coordinates, before the push commits.
        var gate = new CommitGate();
        piston.BeforePushCommit = gate.Pause;
        var tick = Task.Run(async () => await piston.TickAsync());
        var arrival = zombie.Position;
        try
        {
            Assert.Same(gate.Reached.Task, await Task.WhenAny(gate.Reached.Task, tick));
            Assert.Same(zombie, await gate.Reached.Task);
            await zombie.TeleportAsync(destination.World, arrival);
            Assert.Same(destination.World, zombie.Level);
        }
        finally
        {
            gate.Release.TrySetResult();
            piston.BeforePushCommit = null;
            await tick;
        }

        Assert.Equal(arrival, zombie.Position);
        Assert.Same(destination.World, zombie.Level);
    }

    private static Zombie Spawn(WorldData.World world, VectorD position)
    {
        var zombie = new Zombie
        {
            EntityId = Server.GetNextEntityId(),
            Level = world,
            Position = position,
            NoGravity = true
        };
        world.SpawnEntity(zombie);
        return zombie;
    }

    private static PluginMob SpawnPluginMob(WorldData.World world, IEntityBehavior behavior)
    {
        var name = $"travelguards{Guid.NewGuid():N}";
        var owner = new PluginContainer
        {
            Info = new PluginInfo(name),
            Archive = PluginArchive.Open($"{name}.obby", PluginArchives.Zip(new Dictionary<string, byte[]>
            {
                [PluginMetadata.FileName] = PluginArchives.Metadata(name, "1.0.0", "Travel guards")
            }))
        };
        var definition = EntityDefinitionsRegistry.Register($"{name}:mob", EntityKind.Mob);
        var properties = new EntityProperties { Behavior = behavior };
        Assert.True(EntityDefinitionsRegistry.TryBind(definition, properties, owner, () => false));

        var mob = Assert.IsType<PluginMob>(PluginEntity.Create(definition, world));
        mob.EntityId = Server.GetNextEntityId();
        mob.Position = new VectorD(8.5, 70, 8.5);
        mob.NoGravity = true;
        world.SpawnEntity(mob);
        return mob;
    }

    // A plugin's damage hook that, while closed, waits for the test before letting the hit go on.
    private sealed class GatedHurtBehavior : IEntityBehavior
    {
        public bool Open { get; set; }

        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask HurtAsync(IEntity entity, EntityDamage damage)
        {
            if (this.Open)
                return;

            this.Reached.SetResult();
            await this.Release.Task;
        }
    }

    // Holds the ticking thread at a synchronous hook until the test lets it go on.
    private sealed class CommitGate
    {
        internal TaskCompletionSource<Entity> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void Pause(Entity entity)
        {
            this.Reached.SetResult(entity);
            this.Release.Task.GetAwaiter().GetResult();
        }
    }
}
