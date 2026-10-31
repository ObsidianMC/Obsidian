using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Effects;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Entities.Factories;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using Obsidian.Net.Actions.BossBar;
using Obsidian.Net.Packets.Play.Clientbound;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class MobParity
{
    [Fact]
    public async Task MobSavesPreserveHealthAndLoadLegacyState()
    {
        {
            await using var context = TestServerFactory.CreateServer($"mob-absorption-{Guid.NewGuid():N}");
            AddEmptyChunk(context.World);
            var pig = new Pig
            {
                Level = context.World,
                Position = new VectorD(8, 70, 8)
            };
            pig.InitializeAi(false);
            pig.AddPotionEffect((int)PotionEffect.Absorption - 1, 100);

            await pig.DamageCombatAsync(pig, 1.5f, CombatDamageKind.Magic);
            var loaded = Assert.IsType<Pig>(EntityNbt.Load(EntityNbt.Save(pig)!, context.World));
            Assert.Equal(2.5f, loaded.AbsorptionHealth);
            Assert.Equal(10, loaded.Health);
            await loaded.DamageCombatAsync(loaded, 3, CombatDamageKind.Magic);
            Assert.Equal(0, loaded.AbsorptionHealth);
            Assert.Equal(9.5f, loaded.Health);
        }

        {
            await using var context = TestServerFactory.CreateServer($"legacy-mob-{Guid.NewGuid():N}");
            var pig = new Pig
            {
                Level = context.World,
                Position = new VectorD(8, 70, 8)
            };
            pig.InitializeAi(false);
            var saved = EntityNbt.Save(pig)!;
            saved.Remove("TicksFrozen");
            saved.Remove("FallDistance");

            var loaded = Assert.IsType<Pig>(EntityNbt.Load(saved, context.World));

            Assert.Equal(0, loaded.PowderedSnowTicks);
        }
    }

    [Fact]
    public async Task DragonDamageAndDeathSurviveSaving()
    {
        {
            await using var context = TestServerFactory.CreateServer($"dragon-death-{Guid.NewGuid():N}");
            var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Attacker");
            var dragon = new EnderDragon
            {
                Level = context.World,
                Position = new VectorD(8, 100, 8)
            };
            dragon.InitializeAi(false);
            dragon.BeginFight(new VectorD(0, 64, 0));

            await dragon.DamagePartAsync(player, 1000, 0);

            Assert.Equal(1, dragon.Health);
            var saved = EntityNbt.Save(dragon)!;
            Assert.Equal(9, saved.GetInt("DragonPhase"));
            Assert.Equal(0, saved.GetInt("DragonDeathTime"));
            var restored = Assert.IsType<EnderDragon>(EntityNbt.Load(saved, context.World));
            Assert.True(restored.Alive);
            Assert.Equal(1, restored.Health);
        }

        {
            await using var context = TestServerFactory.CreateServer($"dragon-body-{Guid.NewGuid():N}");
            var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Attacker");
            var dragon = new EnderDragon { Level = context.World };
            dragon.InitializeAi(false);

            await dragon.DamageAsync(player, 12);

            Assert.Equal(196, dragon.Health);
        }

        {
            await using var context = TestServerFactory.CreateServer($"dragon-perched-{Guid.NewGuid():N}");
            var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Attacker");
            var dragon = new EnderDragon { Level = context.World };
            dragon.InitializeAi(false);
            var saved = EntityNbt.Save(dragon)!;
            saved.Set(new NbtTag<int>("DragonPhase", 6));
            dragon = Assert.IsType<EnderDragon>(EntityNbt.Load(saved, context.World));

            await dragon.DamagePartAsync(player, 1000, 0);

            Assert.False(dragon.Alive);
            Assert.Equal(9, EntityNbt.Save(dragon)!.GetInt("DragonPhase"));
        }

        {
            await using var context = TestServerFactory.CreateServer($"dragon-explosion-{Guid.NewGuid():N}");
            var dragon = new EnderDragon { Level = context.World };
            var source = new Creeper { Level = context.World };
            dragon.InitializeAi(false);

            await dragon.DamageCombatAsync(source, 12, CombatDamageKind.Explosion);

            Assert.Equal(196, dragon.Health);
        }
    }

    [Fact]
    public async Task InfinitePotionDurationSurvivesTicksAndSaving()
    {
        await using var context = TestServerFactory.CreateServer($"potion-infinite-{Guid.NewGuid():N}");
        var pig = new Pig
        {
            Level = context.World,
            MobBitMask = MobBitmask.NoAi
        };
        pig.InitializeAi(false);
        var effect = (int)PotionEffect.Speed - 1;
        pig.AddPotionEffect(effect, -1);
        pig = Assert.IsType<Pig>(EntityNbt.Load(EntityNbt.Save(pig)!, context.World));

        for (var tick = 0; tick < 10; tick++)
            await pig.TickAsync();

        Assert.Equal(-1, pig.ActivePotionEffects[effect].CurrentDuration);
    }

    // Non-default state each entity must keep across a save and a load.
    [Fact]
    public async Task EntityStateSurvivesSaving()
    {
        (string Name, Func<World, Entity> Create, Action<Entity> Check)[] cases =
        [
            ("a primed TNT's fuse", world => new PrimedTnt { Level = world, Fuse = 37 },
                loaded => Assert.Equal(37, Assert.IsType<PrimedTnt>(loaded).Fuse)),
            ("a weaker effect behind a shorter stronger one", PigWithStackedSpeed, AssertStackedSpeed),
            ("frozen ticks", world => new Pig { Level = world, PowderedSnowTicks = 140 },
                loaded => Assert.Equal(140, loaded.PowderedSnowTicks))
        ];

        await using var context = TestServerFactory.CreateServer($"entity-state-{Guid.NewGuid():N}");

        FixtureCases.AssertAll(
            cases,
            row => row.Name,
            row => AssertSurvivesSaving(context.World, row.Create, row.Check));
    }

    private static void AssertSurvivesSaving(World world, Func<World, Entity> create, Action<Entity> check)
    {
        var saved = EntityNbt.Save(create(world));
        Assert.NotNull(saved);

        var loaded = EntityNbt.Load(saved, world);

        check(Assert.IsAssignableFrom<Entity>(loaded));
    }

    private static Pig PigWithStackedSpeed(World world)
    {
        var pig = new Pig { Level = world, MobBitMask = MobBitmask.NoAi };
        pig.AddPotionEffect(Speed, 100, 0, EntityEffectFlags.ShowParticles | EntityEffectFlags.ShowIcon);
        pig.AddPotionEffect(Speed, 20, 1);

        return pig;
    }

    private static void AssertStackedSpeed(Entity loaded)
    {
        var pig = Assert.IsType<Pig>(loaded);

        Assert.True(pig.ActivePotionEffects.TryGetValue(Speed, out var active));
        Assert.Equal(1, active.EffectData.Amplifier);
        Assert.Equal(20, active.CurrentDuration);
        var hidden = active.EffectData.HiddenEffect;
        Assert.NotNull(hidden);
        Assert.Equal(0, hidden.Amplifier);
        Assert.Equal(100, hidden.Duration);
        Assert.True(hidden.ShowParticles);
        Assert.True(hidden.ShowIcon);
    }

    private static readonly int Speed = (int)PotionEffect.Speed - 1;

    [Fact]
    public async Task WitherBarFollowsTrackingAndNameChanges()
    {
        await using var context = TestServerFactory.CreateServer($"wither-bar-{Guid.NewGuid():N}");
        AddEmptyChunk(context.World);
        var player = TestServerFactory.CreateConnectedPlayer(
            context.Server, context.World, "Viewer", new VectorD(8, 70, 8));
        Assert.True(context.Server.AddPlayer(player));
        Assert.True(context.World.TryAddPlayer(player));
        var client = Assert.IsType<TestClient>(player.Client);
        var wither = new Wither
        {
            EntityId = 1000,
            Level = context.World,
            Position = new VectorD(8, 70, 8),
            NoGravity = true,
            MobBitMask = MobBitmask.NoAi,
            CustomName = new ChatMessage { Text = "First" }
        };
        wither.InitializeAi(false);
        await wither.TickAsync();
        Assert.Empty(client.SentPackets.OfType<BossEventPacket>());
        player.TrackedEntities[wither.EntityId] = wither.Uuid;
        await wither.TickAsync();
        Assert.Contains(client.SentPackets.OfType<BossEventPacket>(), packet => packet.Action is BossBarAddAction);
        client.ClearPackets();

        wither.CustomName.Text = "Second";
        await wither.TickAsync();

        Assert.Contains(
            client.SentPackets.OfType<BossEventPacket>(),
            packet => packet.Action is BossBarUpdateTitleAction { Title.Text: "Second" });
        client.ClearPackets();
        player.TrackedEntities.Clear();
        await wither.TickAsync();
        Assert.Contains(client.SentPackets.OfType<BossEventPacket>(), packet => packet.Action is BossBarRemoveAction);
    }

    private static void AddEmptyChunk(World world)
    {
        var chunk = new Chunk(0, 0);
        chunk.SetChunkStatus(ChunkGenStage.full);
        world.LoadRegionByChunk(0, 0).SetChunk(chunk);
    }
}
