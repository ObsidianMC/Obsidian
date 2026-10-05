using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:zombie")]
public partial class Zombie : PathfinderMob
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
    protected override string? SoundName => "zombie";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override int GetExperienceReward() => (IsBaby ? 12 : 5) +
        Enum.GetValues<Obsidian.API.Inventory.EquipmentSlot>().Sum(slot => GetEquipment(slot).IsAir || GetEquipmentDropChance(slot) > 1 ? 0 : Random.Next(1, 4));
    protected internal override float AttackDamage => base.AttackDamage + GetWeaponDamage(GetEquipment(Obsidian.API.Inventory.EquipmentSlot.MainHand));
    internal override float EyeHeight => IsBaby ? 0.93f : 1.74f;
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
            entity is AgeableMob { IsBaby: true } && entity.MovementFlags.HasFlag(MovementFlags.OnGround)));
    }

    protected override ValueTask TickMobAsync()
    {
        if (Level.LevelData.Difficulty == Difficulty.Peaceful)
            return RemoveAsync();
        var dayTime = Level.DayTime;
        if (Level.DimensionName != "minecraft:overworld" || dayTime is < 0 or >= 12000 ||
            Level.LevelData.Raining || Level.LevelData.Thundering || InWater || Random.NextSingle() * 30 >= 1.2f)
            return default;
        var position = (Vector)EyePosition.Floor();
        if (Terrain.GetSkyLight(position) < 15)
            return default;
        var helmet = GetEquipment(Obsidian.API.Inventory.EquipmentSlot.Helmet);
        if (!helmet.IsAir)
        {
            DamageEquipment(Obsidian.API.Inventory.EquipmentSlot.Helmet, Random.Next(2));
            return default;
        }
        Ignite(8);
        return default;
    }

    protected override void FinalizeSpawn()
    {
        CanPickUpLoot = Random.NextSingle() < 0.55f * SpecialDifficulty;
        PopulateDefaultArmor();
        if (!IsBaby)
            IsBaby = Random.NextSingle() < 0.05f;
        CanBreakDoors = Random.NextSingle() < SpecialDifficulty * 0.1f;
        ReinforcementChance = Random.NextSingle() * 0.1f;
        TryUpdateAttribute("minecraft:generic.knockback_resistance", Random.NextSingle() * 0.05f);
        var rangeBonus = Random.NextDouble() * 1.5 * SpecialDifficulty;
        if (rangeBonus > 1)
            TryUpdateAttribute("minecraft:generic.follow_range", (float)(FollowRange * (1 + rangeBonus)));
        if (Random.NextSingle() < SpecialDifficulty * 0.05f)
        {
            ReinforcementChance += Random.NextSingle() * 0.25f + 0.5f;
            var health = GetAttributeValue("minecraft:generic.max_health") * (Random.NextSingle() * 3 + 2);
            TryUpdateAttribute("minecraft:generic.max_health", health);
            Health = health;
            CanBreakDoors = true;
        }
        if (Random.NextSingle() >= (Level.LevelData.Difficulty == Difficulty.Hard ? 0.05f : 0.01f))
            return;
        var weapon = Random.Next(6) switch
        {
            0 => Material.IronSword,
            1 => Material.IronSpear,
            _ => Material.IronShovel
        };
        var item = ItemsRegistry.GetSingleItem(weapon);
        var durability = Obsidian.API.Inventory.DataComponents.ComponentBuilder.MaxDamage;
        durability.Value = 250;
        item[DataComponentType.MaxDamage] = durability;
        SetEquipment(Obsidian.API.Inventory.EquipmentSlot.MainHand, item);
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
                continue;
            if (Level is not Obsidian.WorldData.AbstractLevel level ||
                level.GetLoadedChunk(blockPosition.X >> 4, blockPosition.Z >> 4) is not { } chunk ||
                chunk.GetLightLevel(blockPosition.X, blockPosition.Y, blockPosition.Z, LightType.Block) != 0)
                continue;
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
        DropItem(Material.RottenFlesh, Random.Next(3));
        if (source is IPlayer && Random.NextSingle() < 0.025f)
            DropItem(Random.Next(3) switch { 0 => Material.IronIngot, 1 => Material.Carrot, _ => Material.Potato });
        foreach (var slot in Enum.GetValues<Obsidian.API.Inventory.EquipmentSlot>())
        {
            var item = GetEquipment(slot);
            if (!item.IsAir && Random.NextSingle() < GetEquipmentDropChance(slot))
                DropItem(item);
        }
        return default;
    }

    protected internal override async ValueTask PerformMeleeAttackAsync(IEntity target)
    {
        var health = target.Health;
        await base.PerformMeleeAttackAsync(target);
        if (target.Health < health && target is Living living && Burning &&
            GetEquipment(Obsidian.API.Inventory.EquipmentSlot.MainHand).IsAir && Random.NextSingle() < EffectiveDifficulty * 0.3f)
            living.Ignite(2 * (int)EffectiveDifficulty);
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.Boolean);
        writer.WriteBoolean(IsBaby);
        writer.WriteEntityMetadataType(17, EntityMetadataType.VarInt);
        writer.WriteVarInt(0);
        writer.WriteEntityMetadataType(18, EntityMetadataType.Boolean);
        writer.WriteBoolean(this is Husk { ConversionTicks: >= 0 });
    }
}
