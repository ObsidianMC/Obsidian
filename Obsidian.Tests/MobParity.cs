using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Effects;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Entities.Factories;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
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
    public async Task MobDamageRespectsImmunitiesAndArmorBypass()
    {
        {
            await using var context = TestServerFactory.CreateServer($"mob-fire-resistance-{Guid.NewGuid():N}");
            AddEmptyChunk(context.World);
            var pig = new Pig
            {
                Level = context.World,
                Position = new VectorD(8, 70, 8)
            };
            pig.InitializeAi(false);
            pig.SetEquipment(EquipmentSlot.Chestplate, ItemsRegistry.GetSingleItem(ItemsRegistry.IronChestplate));
            pig.AddPotionEffect((int)PotionEffect.FireResistance - 1, 100);

            await pig.DamageCombatAsync(pig, 4, CombatDamageKind.FireContact);

            Assert.Equal(10, pig.Health);
            Assert.Equal(0, pig.GetEquipment(EquipmentSlot.Chestplate).Damage);
            Assert.Equal(0, pig.AcceptedDamageCount);
        }

        {
            await using var context = TestServerFactory.CreateServer($"sonic-knockback-{Guid.NewGuid():N}");
            AddEmptyChunk(context.World);
            var warden = new Warden
            {
                Level = context.World,
                Position = new VectorD(4, 70, 8)
            };
            var pig = new Pig
            {
                Level = context.World,
                Position = new VectorD(8, 70, 8)
            };
            warden.InitializeAi(false);
            pig.InitializeAi(false);

            await pig.DamageCombatAsync(warden, 5, CombatDamageKind.SonicBoom);

            Assert.Equal(5, pig.Health);
            Assert.Equal(VectorD.Zero, pig.Motion);
        }

        {
            await using var context = TestServerFactory.CreateServer($"sonic-resistance-{Guid.NewGuid():N}");
            var pig = new Pig { Level = context.World };
            pig.AddPotionEffect((int)PotionEffect.Resistance - 1, 100, 1);

            Assert.Equal(6, CombatEffects.Protect(pig, 10, CombatDamageKind.SonicBoom), 4);
        }

        {
            await using var context = TestServerFactory.CreateServer($"mob-suffocation-{Guid.NewGuid():N}");
            AddEmptyChunk(context.World);
            context.World.GetLoadedChunk(0, 0)!.SetBlock(8, 70, 8, BlocksRegistry.Stone);
            var pig = new Pig
            {
                Level = context.World,
                Position = new VectorD(8.5, 70, 8.5),
                NoGravity = true,
                MobBitMask = MobBitmask.NoAi
            };
            pig.InitializeAi(false);
            pig.SetEquipment(EquipmentSlot.Chestplate, ItemsRegistry.GetSingleItem(ItemsRegistry.DiamondChestplate));

            await pig.TickAsync();

            Assert.Equal(9, pig.Health);
        }
    }

    [Theory]
    [InlineData(EntityType.Camel, 6.9, true)]
    [InlineData(EntityType.Pig, 7.4, false)]
    public async Task MovementAndPathsUseSpeciesStepHeight(EntityType type, double x, bool canStep)
    {
        await using var context = TestServerFactory.CreateServer($"mob-step-{Guid.NewGuid():N}");
        AddEmptyChunk(context.World);
        var chunk = context.World.GetLoadedChunk(0, 0)!;
        for (var blockX = 0; blockX < 16; blockX++)
            for (var z = 0; z < 16; z++)
            {
                chunk.SetBlock(blockX, 69, z, BlocksRegistry.Stone);
                if (blockX < 8)
                    continue;
                chunk.SetBlock(blockX, 70, z, BlocksRegistry.Stone);
                chunk.SetBlock(
                    blockX,
                    71,
                    z,
                    Obsidian.WorldData.Structures.BlockStateParser.TryParse("minecraft:stone_slab[type=bottom]")!);
            }
        var mob = Assert.IsAssignableFrom<PathfinderMob>(EntitySpawner.CreateMob(context.World, type));
        mob.Position = new VectorD(x, 70, 8.5);
        mob.Motion = new VectorD(0.5, -0.08, 0);
        mob.MovementFlags = MovementFlags.OnGround;
        mob.InitializeAi(false);

        var destination = EntityMovement.Move(mob);

        Assert.Equal(canStep, destination.Y >= 71.5);
        Assert.Equal(canStep, new WalkNodeEvaluator(mob).FindGround(8, 8, 70) != null);
        Assert.Equal(!canStep, mob.MovementFlags.HasFlag(MovementFlags.HorizontalCollision));
    }

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
    public async Task OwnerCanEquipRepairAndRemoveWolfArmor()
    {
        {
            await using var context = TestServerFactory.CreateServer($"wolf-armor-{Guid.NewGuid():N}");
            AddEmptyChunk(context.World);
            var player = TestServerFactory.CreateConnectedPlayer(
                context.Server, context.World, "Owner", new VectorD(8, 70, 8));
            var wolf = new Wolf
            {
                Level = context.World,
                Position = new VectorD(9, 70, 8),
                Owner = player.Uuid,
                OrderedToSit = true
            };
            wolf.InitializeAi(false);
            player.Inventory.SetItem(player.CurrentHeldItemSlot, ItemsRegistry.GetSingleItem(ItemsRegistry.WolfArmor));

            await wolf.FeedAsync(player, InteractionHand.MainHand);
            Assert.Equal(ItemsRegistry.WolfArmor, wolf.GetEquipment(EquipmentSlot.Body).Holder);
            var health = wolf.Health;
            await wolf.DamageCombatAsync(player, 9, CombatDamageKind.Melee);
            Assert.Equal(health, wolf.Health);
            Assert.Equal(9, wolf.GetEquipment(EquipmentSlot.Body).Damage);
            wolf.OrderedToSit = true;
            player.Inventory.SetItem(
                player.CurrentHeldItemSlot, ItemsRegistry.GetSingleItem(ItemsRegistry.ArmadilloScute));
            await wolf.FeedAsync(player, InteractionHand.MainHand);
            Assert.Equal(1, wolf.GetEquipment(EquipmentSlot.Body).Damage);
            player.Inventory.SetItem(player.CurrentHeldItemSlot, ItemsRegistry.GetSingleItem(ItemsRegistry.Shears));
            await wolf.FeedAsync(player, InteractionHand.MainHand);
            Assert.True(wolf.GetEquipment(EquipmentSlot.Body).IsAir);
        }

        {
            await using var context = TestServerFactory.CreateServer($"wolf-armor-magic-{Guid.NewGuid():N}");
            AddEmptyChunk(context.World);
            var wolf = new Wolf
            {
                Level = context.World,
                Position = new VectorD(8, 70, 8)
            };
            wolf.InitializeAi(false);
            wolf.SetEquipment(EquipmentSlot.Body, ItemsRegistry.GetSingleItem(ItemsRegistry.WolfArmor));

            await wolf.DamageCombatAsync(wolf, 3, CombatDamageKind.Magic);

            Assert.Equal(5, wolf.Health);
            Assert.Equal(0, wolf.GetEquipment(EquipmentSlot.Body).Damage);
        }
    }

    [Fact]
    public async Task IronGolemRepairConsumesAnIngotOnlyWhenDamaged()
    {
        await using var context = TestServerFactory.CreateServer($"golem-repair-{Guid.NewGuid():N}");
        AddEmptyChunk(context.World);
        var player = TestServerFactory.CreateConnectedPlayer(
            context.Server, context.World, "Repairer", new VectorD(8, 70, 8));
        var golem = new IronGolem
        {
            Level = context.World,
            Position = new VectorD(9, 70, 8)
        };
        golem.InitializeAi(false);
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.IronIngot, 3));
        golem.Health = 90;

        await golem.InteractAsync(player, InteractionHand.MainHand);
        await golem.InteractAsync(player, InteractionHand.MainHand);

        Assert.Equal(100, golem.Health);
        Assert.Equal(2, player.GetHeldItem()!.Count);
    }

    [Theory]
    [InlineData(EntityType.Wither)]
    [InlineData(EntityType.Allay)]
    public async Task IdleNavigationPreservesDirectFlightCommands(EntityType type)
    {
        await using var context = TestServerFactory.CreateServer($"direct-flight-{Guid.NewGuid():N}");
        var mob = EntitySpawner.CreateMob(context.World, type)!;
        mob.Position = new VectorD(4, 70, 8);
        mob.InitializeAi(false);
        mob.MoveControl.MoveTo(new VectorD(10, 70, 8), 1);

        Assert.IsType<Navigator>(mob.Navigator).Tick();
        mob.MoveControl.Tick();

        Assert.True(mob.MoveControl.Acceleration.X > 0);
        Assert.IsType<Navigator>(mob.Navigator).IsPaused = true;
        mob.MoveControl.MoveTo(new VectorD(10, 70, 8), 1);
        Assert.IsType<Navigator>(mob.Navigator).Tick();
        mob.MoveControl.Tick();
        Assert.Equal(VectorD.Zero, mob.MoveControl.Acceleration);
    }

    [Fact]
    public async Task WitherChargeAndTargeting()
    {
        {
            await using var context = TestServerFactory.CreateServer($"wither-charge-{Guid.NewGuid():N}");
            AddEmptyChunk(context.World);
            var wither = new Wither
            {
                EntityId = 1,
                Level = context.World,
                Position = new VectorD(4, 70, 8)
            };
            var pig = new Pig
            {
                EntityId = 2,
                Level = context.World,
                Position = new VectorD(8, 70, 8)
            };
            wither.InitializeAi(false);
            pig.InitializeAi(false);
            Assert.True(context.World.TryAddEntity(pig));
            wither.BeginSpawnCharge();
            wither.AlertedTarget = pig;

            for (var tick = 0; tick < 5; tick++)
                await wither.TickAsync();

            Assert.Null(wither.AttackTarget);
            Assert.Equal(100, wither.Health);
            await wither.DamageAsync(pig, 20);
            Assert.Equal(100, wither.Health);
        }

        {
            await using var context = TestServerFactory.CreateServer($"wither-flight-{Guid.NewGuid():N}");
            AddEmptyChunk(context.World);
            var wither = new Wither
            {
                EntityId = 1,
                Level = context.World,
                Position = new VectorD(4, 70, 8)
            };
            var pig = new Pig
            {
                EntityId = 2,
                Level = context.World,
                Position = new VectorD(10, 70, 8)
            };
            wither.InitializeAi(false);
            pig.InitializeAi(false);
            wither.AttackTarget = pig;
            await wither.TickAsync();

            Assert.True(wither.Position.X > 4);
            Assert.True(wither.Position.Y > 70);
            Assert.Empty(context.World.GetEntitiesInRange(wither.Position, 32));
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

    [Theory]
    [InlineData(false, Difficulty.Normal, 1)]
    [InlineData(true, Difficulty.Hard, 5)]
    public async Task GuardianBeamHasAnArmorBypassingComponent(bool elder, Difficulty difficulty, float magicDamage)
    {
        await using var context = TestServerFactory.CreateServer($"guardian-beam-{Guid.NewGuid():N}");
        AddEmptyChunk(context.World);
        context.World.LevelData.Difficulty = difficulty;
        Guardian guardian = elder
            ? new ElderGuardian { Level = context.World }
            : new Guardian { Level = context.World };
        guardian.Position = new VectorD(4, 70, 8);
        guardian.InitializeAi(false);
        var pig = new Pig
        {
            Level = context.World,
            Position = new VectorD(10, 70, 8)
        };
        pig.InitializeAi(false);
        pig.Health = 100;
        pig.SetEquipment(EquipmentSlot.Helmet, ItemsRegistry.GetSingleItem(ItemsRegistry.DiamondHelmet));
        pig.SetEquipment(EquipmentSlot.Chestplate, ItemsRegistry.GetSingleItem(ItemsRegistry.DiamondChestplate));
        pig.SetEquipment(EquipmentSlot.Leggings, ItemsRegistry.GetSingleItem(ItemsRegistry.DiamondLeggings));
        pig.SetEquipment(EquipmentSlot.Boots, ItemsRegistry.GetSingleItem(ItemsRegistry.DiamondBoots));
        guardian.AttackTarget = pig;
        var goal = new GuardianAttackGoal(guardian);
        goal.Start();

        for (var tick = 0; tick < guardian.AttackDuration + 10; tick++)
            await goal.TickAsync();

        var damage = magicDamage + CombatItems.ReduceArmor(guardian.AttackDamage - magicDamage, 20, 8);
        Assert.Equal(100 - damage, pig.Health, 4);
        Assert.Null(guardian.AttackTarget);
    }

    [Fact]
    public async Task SummonedDragonHoversAndItsPartsFaceTheVanillaDirection()
    {
        await using var context = TestServerFactory.CreateServer($"dragon-hover-{Guid.NewGuid():N}");
        AddEmptyChunk(context.World);
        var dragon = new EnderDragon
        {
            Level = context.World,
            Position = new VectorD(8, 70, 8)
        };
        dragon.InitializeAi(false);
        dragon.EnsureMultipartIds();
        var start = dragon.Position;

        for (var tick = 0; tick < 25; tick++)
            await dragon.TickAsync();

        Assert.Equal(start, dragon.Position);
        Assert.Equal(10, EntityNbt.Save(dragon)!.GetInt("DragonPhase"));
        Assert.Equal(start + new VectorD(0, -1, -6.5), dragon.Parts[0].Position);
        Assert.Equal(start + new VectorD(0, 0, -0.5), dragon.Parts[2].Position);
        Assert.Equal(start + new VectorD(0, 1.5, 3.5), dragon.Parts[3].Position);
    }

    [Theory]
    [InlineData(false, BlockIds.ShortGrass)]
    [InlineData(true, BlockIds.GrassBlock)]
    public async Task SheepRegrowWoolWithoutGriefingGrass(bool griefing, int grass)
    {
        await using var context = TestServerFactory.CreateServer($"sheep-griefing-{Guid.NewGuid():N}");
        AddEmptyChunk(context.World);
        context.World.LevelData.GameRules["mob_griefing"] = griefing.ToString();
        var y = grass == BlockIds.GrassBlock ? 69 : 70;
        context.World.GetLoadedChunk(0, 0)!.SetBlock(8, y, 8, BlocksRegistry.GetDefault(grass));
        var sheep = new Sheep
        {
            Level = context.World,
            Position = new VectorD(8, 70, 8),
            Sheared = true
        };
        sheep.InitializeAi(false);
        var goal = new EatGrassGoal(sheep);
        goal.Start();

        for (var tick = 0; tick < 36; tick++)
            await goal.TickAsync();

        Assert.False(sheep.Sheared);
        Assert.Equal(griefing ? grass == BlockIds.ShortGrass ? BlockIds.Air : BlockIds.Dirt : grass,
            context.World.GetLoadedChunk(0, 0)!.GetBlock(new Vector(8, y, 8)).RegistryId);
    }

    [Fact]
    public async Task SilverfishRestoreHostBlocksWhenGriefingIsDisabled()
    {
        await using var context = TestServerFactory.CreateServer($"silverfish-griefing-{Guid.NewGuid():N}");
        AddEmptyChunk(context.World);
        context.World.LevelData.GameRules["mob_griefing"] = "false";
        for (var x = 0; x < 16; x++)
            for (var z = 0; z < 16; z++)
                context.World.GetLoadedChunk(0, 0)!.SetBlock(x, 69, z, BlocksRegistry.Stone);
        context.World.GetLoadedChunk(0, 0)!.SetBlock(8, 70, 9, BlocksRegistry.InfestedStone);
        var silverfish = new Silverfish
        {
            Level = context.World,
            Position = new VectorD(8, 70, 8)
        };
        var pig = new Pig
        {
            Level = context.World,
            Position = new VectorD(8, 70, 10)
        };
        silverfish.InitializeAi(false);
        await silverfish.DamageAsync(pig, 1);

        for (var tick = 0; tick < 20; tick++)
            await silverfish.TickAsync();

        Assert.Equal(BlockIds.Stone, context.World.GetLoadedChunk(0, 0)!.GetBlock(new Vector(8, 70, 9)).RegistryId);
        Assert.Empty(context.World.GetEntitiesInRange(silverfish.Position, 32));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShorterStrongerPotionRestoresTheRemainingWeakerEffect(bool reload)
    {
        await using var context = TestServerFactory.CreateServer($"potion-stacking-{Guid.NewGuid():N}");
        var pig = new Pig
        {
            Level = context.World,
            MobBitMask = MobBitmask.NoAi
        };
        pig.InitializeAi(false);
        var effect = (int)PotionEffect.Speed - 1;
        pig.AddPotionEffect(effect, 100, 0, EntityEffectFlags.ShowParticles | EntityEffectFlags.ShowIcon);
        pig.AddPotionEffect(effect, 20, 1);
        if (reload)
            pig = Assert.IsType<Pig>(EntityNbt.Load(EntityNbt.Save(pig)!, context.World));

        for (var tick = 0; tick < 20; tick++)
            await pig.TickAsync();

        Assert.True(pig.ActivePotionEffects.TryGetValue(effect, out var remaining));
        Assert.Equal(0, remaining.EffectData.Amplifier);
        Assert.Equal(80, remaining.CurrentDuration);
        Assert.True(remaining.EffectData.ShowParticles);
        Assert.True(remaining.EffectData.ShowIcon);
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

    [Theory]
    [InlineData(EntityType.Pig, false, 140, 99)]
    [InlineData(EntityType.Pig, true, 0, 100)]
    public async Task PowderSnowRespectsFreezingImmunities(EntityType type, bool leather, int frozen, float health)
    {
        await using var context = TestServerFactory.CreateServer($"mob-freezing-{Guid.NewGuid():N}");
        AddEmptyChunk(context.World);
        context.World.GetLoadedChunk(0, 0)!.SetBlock(8, 70, 8, BlocksRegistry.PowderSnow);
        var mob = EntitySpawner.CreateMob(context.World, type)!;
        mob.Position = new VectorD(8.5, 70, 8.5);
        mob.NoGravity = true;
        mob.MobBitMask = MobBitmask.NoAi;
        mob.InitializeAi(false);
        mob.Health = 100;
        if (leather)
            mob.SetEquipment(EquipmentSlot.Helmet, ItemsRegistry.GetSingleItem(ItemsRegistry.LeatherHelmet));

        for (var tick = 0; tick < 160; tick++)
            await mob.TickAsync();

        Assert.Equal(frozen, mob.PowderedSnowTicks);
        Assert.Equal(health, mob.Health);
        Assert.Equal(frozen, EntityNbt.Load(EntityNbt.Save(mob)!, context.World)!.PowderedSnowTicks);
    }

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
