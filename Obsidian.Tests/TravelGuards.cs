using Obsidian.API;
using Obsidian.API.Entities;
using Obsidian.API.Inventory;
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
using ServerPlayer = Obsidian.Entities.Player;

namespace Obsidian.Tests;

/// <summary>
/// Work that awaits between finding an entity and changing it must not change it once it has travelled to another
/// level, or otherwise stopped being something it may change, meanwhile. Each test makes that happen at the exact
/// point between the two, held there by a gate.
/// </summary>
public sealed class TravelGuards
{
    [Fact]
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

    [Fact]
    public async Task DamageSkipsMobDestroyedWhileAPluginHookAwaits()
    {
        await using var context = TestServerFactory.CreateServer($"damage-destroyed-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        var hook = new GatedHurtBehavior();
        var mob = SpawnPluginMob(context.World, hook);
        var attacker = Spawn(context.World, new VectorD(10.5, 70, 8.5));
        var health = mob.Health;

        // The level takes the mob out while the hook holds the hit; nothing marks the mob itself as removed.
        var hit = mob.DamageCombatAsync(attacker, 6, CombatDamageKind.Melee).AsTask();
        await hook.Reached.Task;
        Assert.True(await context.World.DestroyEntityAsync(mob));
        Assert.False(mob.IsRemoved);
        hook.Release.SetResult();
        await hit;

        Assert.Equal(health, mob.Health);
        Assert.Equal(VectorD.Zero, mob.Motion);
        Assert.Equal(0, mob.AcceptedDamageCount);
    }

    [Fact]
    public async Task BurnSkipsArmorStandTheLevelDestroyed()
    {
        await using var context = TestServerFactory.CreateServer($"burn-destroyed-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        var stand = new ArmorStand
        {
            EntityId = Server.GetNextEntityId(),
            Level = context.World,
            Position = new VectorD(8.5, 70, 8.5)
        };
        context.World.SpawnEntity(stand);
        await stand.DamageByBurningAsync(4);
        Assert.Equal(16, stand.Health);

        // The level takes the stand out without marking it removed; burning has no await, so it's burnt after.
        Assert.True(await context.World.DestroyEntityAsync(stand));
        Assert.False(stand.IsRemoved);
        await stand.DamageByBurningAsync(4);

        Assert.Equal(16, stand.Health);
    }

    [Fact]
    public async Task PlayerHitSkipsPlayerTurnedCreativeDuringArmourWear()
    {
        await using var context = TestServerFactory.CreateServer($"damage-creative-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        var player = await JoinArmouredPlayerAsync(context);
        var attacker = Spawn(context.World, new VectorD(10.5, 70, 8.5));

        // The hit passes every check before the wear, then waits to wear the chestplate; the player turns Creative.
        var hold = await InventoryHold.TakeAsync(player);
        var hit = player.DamageCombatAsync(attacker, 4, CombatDamageKind.Melee).AsTask();
        Assert.False(hit.IsCompleted);
        player.GameMode = GameMode.Creative;
        await hold.ReleaseAsync();
        await hit;

        Assert.Equal(20, player.Health);
        Assert.Equal(0, player.HurtTime);
        Assert.Equal(0, player.AcceptedDamageCount);
    }

    [Fact]
    public async Task PlayerHitDuringArmourWearYieldsToAHitThatLandedMeanwhile()
    {
        await using var context = TestServerFactory.CreateServer($"damage-cooldown-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        var player = await JoinArmouredPlayerAsync(context);
        var attacker = Spawn(context.World, new VectorD(10.5, 70, 8.5));

        // The first hit waits to wear the chestplate; a hit that wears nothing lands meanwhile.
        var hold = await InventoryHold.TakeAsync(player);
        var first = player.DamageCombatAsync(attacker, 4, CombatDamageKind.Melee).AsTask();
        Assert.False(first.IsCompleted);
        await player.DamageCombatAsync(attacker, 4, CombatDamageKind.Magic);
        Assert.Equal(16, player.Health);

        // The first hit is no bigger than the one that landed, so the hurt cooldown takes all of it.
        await hold.ReleaseAsync();
        await first;

        Assert.Equal(16, player.Health);
        Assert.Equal(1, player.AcceptedDamageCount);
    }

    [Fact]
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

    [Fact]
    public async Task SlimePistonSkipsLaunchingEntityTransferredMeanwhile()
    {
        await using var source = TestServerFactory.CreateServer($"slime-travel-source-{Guid.NewGuid():N}");
        await using var destination = TestServerFactory.CreateServer($"slime-travel-destination-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(source.World);
        WorldBlockEntities.AddEmptyChunk(destination.World);
        var slime = BlocksRegistry.GetDefault(BlockIds.SlimeBlock);
        var piston = new MovingPistonBlockEntity { BlockPosition = new Vector(8, 70, 8) };
        piston.Configure(source.World, slime, BlockFace.East, extending: true, source: false);
        var pushed = Spawn(source.World, new VectorD(8.6, 70, 8.5));
        var traveller = Spawn(source.World, new VectorD(8.6, 70, 8.5));

        // The first zombie's push waits while the second, already found in the slime's path, travels away.
        var gate = new CommitGate();
        piston.BeforePushCommit = entity =>
        {
            if (ReferenceEquals(entity, pushed))
                gate.Pause(entity);
        };
        var tick = Task.Run(async () => await piston.TickAsync());
        var arrival = traveller.Position;
        try
        {
            Assert.Same(gate.Reached.Task, await Task.WhenAny(gate.Reached.Task, tick));
            await traveller.TeleportAsync(destination.World, arrival);
            Assert.Same(destination.World, traveller.Level);
        }
        finally
        {
            gate.Release.TrySetResult();
            piston.BeforePushCommit = null;
            await tick;
        }

        Assert.Equal(1, pushed.Motion.X);
        Assert.Equal(VectorD.Zero, traveller.Motion);
        Assert.Equal(arrival, traveller.Position);
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

    private static async Task<ServerPlayer> JoinArmouredPlayerAsync(RealServerContext context)
    {
        var player = TestServerFactory.CreateConnectedPlayer(
            context.Server,
            context.World,
            "Armoured",
            new VectorD(8.5, 70, 8.5));
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, player);
        player.GameMode = GameMode.Survival;
        player.Inventory.SetItem(6, new ItemStack(ItemsRegistry.IronChestplate));
        return player;
    }

    // Holds a player's inventory from another flow, so a hit wearing their armour waits for it.
    private sealed class InventoryHold
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task holder = Task.CompletedTask;

        internal static async Task<InventoryHold> TakeAsync(ServerPlayer player)
        {
            var hold = new InventoryHold();
            var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            hold.holder = Task.Run(async () =>
            {
                using (await player.Inventory.LockAsync())
                {
                    held.SetResult();
                    await hold.release.Task;
                }
            });
            await held.Task;
            return hold;
        }

        internal Task ReleaseAsync()
        {
            this.release.TrySetResult();
            return this.holder;
        }
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
