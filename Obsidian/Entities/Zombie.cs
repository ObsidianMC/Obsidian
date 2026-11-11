using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:zombie")]
public partial class Zombie : PathfinderMob, IAgeable
{
    public Zombie() => Type = EntityType.Zombie;

    protected override bool UsesAi => true;
    protected override float DimensionScale => IsBaby ? 0.5f : 1;
    public bool IsBaby { get; set; }
    private bool canBreakDoors;
    public bool CanBreakDoors
    {
        get => canBreakDoors;
        set
        {
            canBreakDoors = value;
            if (Navigator is Navigator navigation)
                navigation.CanOpenDoors = value;
        }
    }
    public float ReinforcementChance { get; internal set; }
    internal int DrowningWaterTicks { get; set; } = -1;
    internal int DrowningConversionTicks { get; set; } = -1;
    protected override string? SoundName => "zombie";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override int GetExperienceReward() => (IsBaby ? 12 : 5) +
        Enum.GetValues<Obsidian.API.Inventory.EquipmentSlot>().Sum(slot => GetEquipment(slot).IsAir || GetEquipmentDropChance(slot) > 1 ? 0 : Random.Next(1, 4));
    protected internal override float AttackDamage => base.AttackDamage + GetWeaponDamage(GetEquipment(Obsidian.API.Inventory.EquipmentSlot.MainHand));
    internal override float EyeHeight => (IsBaby ? 0.93f : 1.74f) * this.AttributeScale;
    internal override float MovementSpeed => base.MovementSpeed * (IsBaby ? 1.5f : 1);

    protected override void RegisterGoals(GoalSelector actionGoals, GoalSelector targetGoals)
    {
        ((Navigator)Navigator!).CanOpenDoors = CanBreakDoors;
        actionGoals.AddGoal(1, new BreakDoorGoal(this));
        actionGoals.AddGoal(3, new ZombieAttackGoal(this));
        actionGoals.AddGoal(7, new RandomStrollGoal(this, 1));
        actionGoals.AddGoal(8, new LookAtPlayerGoal(this, 8));
        actionGoals.AddGoal(8, new RandomLookAroundGoal(this));
        targetGoals.AddGoal(1, new HurtByTargetGoal(this));
        targetGoals.AddGoal(2, new NearestAttackableTargetGoal(this, entity => entity is IPlayer));
        targetGoals.AddGoal(3, new NearestAttackableTargetGoal(this, entity => entity.Type == EntityType.Villager, mustSee: false));
        targetGoals.AddGoal(3, new NearestAttackableTargetGoal(this, entity => entity.Type == EntityType.IronGolem));
        targetGoals.AddGoal(5, new NearestAttackableTargetGoal(this, entity => entity.Type == EntityType.Turtle &&
            entity is IAgeable { IsBaby: true } && entity.MovementFlags.HasFlag(MovementFlags.OnGround)));
    }

    protected override async ValueTask TickMobAsync()
    {
        if (Level.LevelData.Difficulty == Difficulty.Peaceful)
        {
            await RemoveAsync();
            return;
        }
        if (Type == EntityType.Zombie && !MobBitMask.HasFlag(MobBitmask.NoAi))
        {
            if (DrowningConversionTicks >= 0)
            {
                if (--DrowningConversionTicks < 0)
                {
                    await ConvertToAsync(EntityType.Drowned);
                    return;
                }
            }
            else if (Terrain.GetBlock((Vector)EyePosition.Floor())?.RegistryId == BlockIds.Water)
            {
                if (++DrowningWaterTicks >= 600)
                {
                    DrowningConversionTicks = 300;
                    SynchronizeMetadata();
                }
            }
            else
            {
                DrowningWaterTicks = -1;
            }
        }
        if (!Level.MonstersBurn((Vector)Position.Floor()) ||
            Level.LevelData.Raining || Level.LevelData.Thundering || InWater || Random.NextSingle() * 30 >= 1.2f)
        {
            return;
        }

        var position = (Vector)EyePosition.Floor();
        if (Terrain.GetSkyLight(position) < 15)
            return;
        var helmet = GetEquipment(Obsidian.API.Inventory.EquipmentSlot.Helmet);
        if (!helmet.IsAir)
        {
            DamageEquipment(Obsidian.API.Inventory.EquipmentSlot.Helmet, Random.Next(2));
            return;
        }
        Ignite(8);
    }

    protected override void FinalizeSpawn()
    {
        CanPickUpLoot = Random.NextSingle() < 0.55f * SpecialDifficulty;
        PopulateDefaultArmor();
        if (!IsBaby)
            IsBaby = Random.NextSingle() < 0.05f;
        CanBreakDoors = Random.NextSingle() < SpecialDifficulty * 0.1f;
        HandleSpawnAttributes(SpecialDifficulty);
        PopulateDefaultWeapon(Random.NextSingle, Random.Next);
    }

    /// <summary>
    /// Vanilla's <c>Zombie.finalizeSpawn</c> for a spawn that isn't a conversion, drawing from the level's random
    /// (<paramref name="random"/>) rather than the mob's own, in vanilla's order: <c>Mob</c>'s spawn draws, picking up
    /// loot, being a baby (and then riding a chicken nearby, or a new chicken jockey), breaking doors, the default
    /// equipment, its enchantments and, on Halloween, a pumpkin. The draws and their order are part of the level's
    /// random sequence; the attribute bonuses after them come from the mob's own random, as vanilla's
    /// <c>handleAttributes</c> does.
    /// </summary>
    /// <returns>A new chicken the zombie rides, which the caller adds to the level before the zombie.</returns>
    internal Chicken? FinalizeSpawnFromLevel(AbstractLevel level, IRandomSource random)
    {
        var special = SpecialDifficulty;

        FinalizeMobSpawnFromLevel(random);
        CanPickUpLoot = random.NextFloat() < 0.55f * special;
        Chicken? jockey = null;
        if (random.NextFloat() < 0.05f)
        {
            IsBaby = true;
            if (random.NextFloat() < 0.05f)
                RideChickenNearby(level);
            else if (random.NextFloat() < 0.05f)
                jockey = NewChickenJockey(level, random);
        }

        CanBreakDoors = random.NextFloat() < special * 0.1f;
        PopulateDefaultEquipmentFromLevel(random, special);
        PopulateEquipmentEnchantmentsFromLevel(random, special);
        PutOnHalloweenHeadFromLevel(level, random);
        HandleSpawnAttributes(special);
        return jockey;
    }

    /// <summary>A spawner's bare zombie, set up as vanilla's <c>Zombie.finalizeSpawn</c> does.</summary>
    internal override SpawnCompanions FinalizeBareSpawn(AbstractLevel level, IRandomSource random) =>
        new(Vehicle: FinalizeSpawnFromLevel(level, random), Passenger: null);

    /// <summary>
    /// Vanilla's <c>Zombie.populateDefaultEquipmentSlots</c> from the level's random: <c>Mob</c>'s armour, then the
    /// weapon.
    /// </summary>
    private protected virtual void PopulateDefaultEquipmentFromLevel(IRandomSource random, float special)
    {
        PopulateDefaultArmor(random.NextFloat, random.NextInt, special);
        PopulateDefaultWeapon(random.NextFloat, random.NextInt);
    }

    // Vanilla's zombie weapon: one time in 20 on hard, one in 100 otherwise, an iron sword, an iron spear or (four
    // times in six) an iron shovel.
    private void PopulateDefaultWeapon(Func<float> nextFloat, Func<int, int> nextInt)
    {
        if (nextFloat() >= (Level.LevelData.Difficulty == Difficulty.Hard ? 0.05f : 0.01f))
            return;

        var weapon = nextInt(6) switch
        {
            0 => ItemsRegistry.IronSword,
            1 => ItemsRegistry.IronSpear,
            _ => ItemsRegistry.IronShovel
        };
        var item = ItemsRegistry.GetSingleItem(weapon);
        var durability = Obsidian.API.Inventory.DataComponents.ComponentBuilder.MaxDamage with { Value = 250 };
        item[DataComponentType.MaxDamage] = durability;
        SetEquipment(Obsidian.API.Inventory.EquipmentSlot.MainHand, item);
    }

    /// <summary>
    /// Vanilla's <c>Zombie.handleAttributes</c>, from the mob's own random: its chance to call reinforcements, a
    /// little knockback resistance, perhaps a longer follow range and, rarely, a leader's bonuses.
    /// </summary>
    private protected virtual void HandleSpawnAttributes(float special)
    {
        ReinforcementChance = Random.NextSingle() * 0.1f;
        TryUpdateAttribute("minecraft:generic.knockback_resistance", Random.NextSingle() * 0.05f);
        var rangeBonus = Random.NextDouble() * 1.5 * special;
        if (rangeBonus > 1)
            TryUpdateAttribute("minecraft:generic.follow_range", (float)(FollowRange * (1 + rangeBonus)));
        if (Random.NextSingle() < special * 0.05f)
        {
            ReinforcementChance += Random.NextSingle() * 0.25f + 0.5f;
            var health = GetAttributeValue("minecraft:generic.max_health") * (Random.NextSingle() * 3 + 2);
            TryUpdateAttribute("minecraft:generic.max_health", health);
            Health = health;
            CanBreakDoors = true;
        }
    }

    // Vanilla takes the first eligible chicken in the entity query, without a draw or nearest-distance sort:
    // alive, carrying nobody and riding nothing, within 5 blocks across and 3 up or down of the zombie's box.
    // A successful first jockey roll with no eligible chicken does not try the second roll.
    private void RideChickenNearby(AbstractLevel level)
    {
        var box = Dimension.CreateBBFromPosition(Position);
        var reach = new BoundingBox(box.Min - new VectorD(5, 3, 5), box.Max + new VectorD(5, 3, 5));
        var chicken = level.GetNonPlayerEntitiesInRange(Position, 16)
            .OfType<Chicken>()
            .FirstOrDefault(chicken => chicken.Alive && !chicken.IsRemoved
                && chicken.Passengers.Count == 0 && chicken.Vehicle is null
                && reach.Intersects(chicken.Dimension.CreateBBFromPosition(chicken.Position)));
        if (chicken is null)
            return;

        chicken.IsChickenJockey = true;
        StartRiding(chicken);
    }

    // Vanilla's second jockey chance: a new chicken where the zombie stands, turned its way, finalized from the
    // level's random, which the zombie rides.
    private Chicken NewChickenJockey(AbstractLevel level, IRandomSource random)
    {
        var chicken = (Chicken)Factories.EntitySpawner.CreateMob(level, EntityType.Chicken)!;
        chicken.EntityId = Server.GetNextEntityId();
        chicken.Position = Position;
        chicken.Yaw = Yaw;
        chicken.Pitch = 0;
        chicken.InitializeAi(finalizeSpawn: false);
        chicken.FinalizeJockeySpawnFromLevel(random);
        chicken.IsChickenJockey = true;
        return chicken;
    }

    protected override ValueTask OnHurtAsync(IEntity source)
    {
        var target = AttackTarget ?? (IsValidTarget(source) ? source : null);
        if (target == null || Level.LevelData.Difficulty != Difficulty.Hard || Random.NextSingle() >= ReinforcementChance)
            return default;
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var point = new VectorD(Math.Floor(Position.X) + Random.Next(7, 41) * Random.Next(-1, 2) + 0.5f,
                Math.Floor(Position.Y) + Random.Next(7, 41) * Random.Next(-1, 2),
                Math.Floor(Position.Z) + Random.Next(7, 41) * Random.Next(-1, 2) + 0.5f);
            var blockPosition = (Vector)point.Floor();
            var floor = Terrain.GetBlock(blockPosition - new Vector(0, 1, 0));
            var feet = Terrain.GetBlock(blockPosition);
            var bounds = new EntityDimension { Width = 0.6f, Height = 1.95f }.CreateBBFromPosition(point);
            if (floor == null || feet == null || feet.IsLiquid || floor.IsLiquid ||
                BlockCollisionShapes.Get(floor).Count == 0 || !Terrain.IsFree(bounds) ||
                Level.GetPlayersInRange(point, 7).Any() || Level.GetEntitiesInRange(point, 2)
                    .Any(entity => MobTerrain.Overlaps(bounds, entity.Dimension.CreateBBFromPosition(entity.Position))))
            {
                continue;
            }

            if (Level is not Obsidian.WorldData.AbstractLevel level ||
                level.GetLoadedChunk(blockPosition.X >> 4, blockPosition.Z >> 4) is not { } chunk ||
                chunk.GetLightLevel(blockPosition.X, blockPosition.Y, blockPosition.Z, LightType.Block) != 0)
            {
                continue;
            }

            var reinforcement = (Zombie)Level.GetNewEntitySpawner().WithEntityType(EntityType.Zombie).AtPosition(point).Spawn();
            reinforcement.AlertedTarget = target;
            reinforcement.ReinforcementChance -= 0.05f;
            ReinforcementChance -= 0.05f;
            break;
        }
        return default;
    }

    protected override ValueTask OnDeathAsync(IEntity source)
    {
        this.DropVanillaLoot(source);
        foreach (var slot in Enum.GetValues<Obsidian.API.Inventory.EquipmentSlot>())
        {
            var item = this.GetEquipment(slot);
            if (!item.IsAir && this.Random.NextSingle() < this.GetEquipmentDropChance(slot))
                this.DropItem(item);
        }

        return default;
    }

    protected internal override async ValueTask PerformMeleeAttackAsync(IEntity target)
    {
        var health = target.Health;
        await base.PerformMeleeAttackAsync(target);
        if (target.Health < health && target is Living living && Burning &&
            GetEquipment(Obsidian.API.Inventory.EquipmentSlot.MainHand).IsAir && Random.NextSingle() < EffectiveDifficulty * 0.3f)
        {
            living.Ignite(2 * (int)EffectiveDifficulty);
        }
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.Boolean);
        writer.WriteBoolean(IsBaby);
        writer.WriteEntityMetadataType(17, EntityMetadataType.VarInt);
        writer.WriteVarInt(0);
        writer.WriteEntityMetadataType(18, EntityMetadataType.Boolean);
        writer.WriteBoolean(DrowningConversionTicks >= 0 || this is Husk { ConversionTicks: >= 0 });
    }
}
