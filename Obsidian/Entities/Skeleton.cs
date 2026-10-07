using Obsidian.API.Inventory;
using Obsidian.Entities.AI;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:skeleton")]
public partial class Skeleton : PathfinderMob
{
    public Skeleton() => Type = EntityType.Skeleton;
    protected override bool UsesAi => true;
    protected override string? SoundName => "skeleton";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override int GetExperienceReward() => 5;
    internal virtual int ArrowEffect => -1;
    internal virtual int ArrowEffectDuration => 0;
    internal virtual int AttackInterval => Level.LevelData.Difficulty == Difficulty.Hard ? 20 : 40;
    protected virtual bool BurnsInSun => true;
    internal int PowderSnowTicks { get; set; } = -1;
    internal SkeletonHorse? HorseVehicle { get; set; }
    protected override VectorD Travel()
    {
        if (HorseVehicle is { Alive: true, IsRemoved: false } horse && horse.Level == Level)
        {
            Motion = VectorD.Zero;
            return horse.Position + new VectorD(0, horse.Dimension.Height * 0.75f, 0);
        }
        HorseVehicle = null;
        return base.Travel();
    }
    internal int StrayConversionTicks { get; set; } = -1;

    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(3, new SkeletonShelterGoal(this));
        actions.AddGoal(3, new AvoidEntityGoal(this, entity => entity.Type == EntityType.Wolf, 6, 1.2f));
        actions.AddGoal(4, new BowAttackGoal(this));
        actions.AddGoal(4, new SkeletonMeleeGoal(this));
        actions.AddGoal(5, new RandomStrollGoal(this, 1));
        actions.AddGoal(6, new LookAtPlayerGoal(this, 8));
        actions.AddGoal(6, new RandomLookAroundGoal(this));
        targets.AddGoal(1, new HurtByTargetGoal(this));
        targets.AddGoal(2, new NearestAttackableTargetGoal(this, entity => entity is IPlayer));
        targets.AddGoal(3, new NearestAttackableTargetGoal(this, entity => entity.Type == EntityType.IronGolem));
        targets.AddGoal(3, new NearestAttackableTargetGoal(this, entity => entity.Type == EntityType.Turtle &&
            entity is AgeableMob { IsBaby: true } && entity.MovementFlags.HasFlag(MovementFlags.OnGround)));
    }

    protected override void FinalizeSpawn()
    {
        CanPickUpLoot = Random.NextSingle() < 0.55f * SpecialDifficulty;
        PopulateDefaultArmor();
        SetEquipment(EquipmentSlot.MainHand, ItemsRegistry.GetSingleItem(Material.Bow));
    }

    protected override async ValueTask TickMobAsync()
    {
        if (Level.LevelData.Difficulty == Difficulty.Peaceful)
        {
            await RemoveAsync();
            return;
        }
        if (Type == EntityType.Skeleton)
        {
            if (Terrain.GetBlock((Vector)Position.Floor())?.Material == Material.PowderSnow)
            {
                if (StrayConversionTicks >= 0)
                {
                    if (--StrayConversionTicks < 0)
                    {
                        await ConvertToAsync(EntityType.Stray);
                        return;
                    }
                }
                else if (++PowderSnowTicks >= 140)
                {
                    StrayConversionTicks = 300;
                    SynchronizeMetadata();
                }
            }
            else
            {
                var wasConverting = StrayConversionTicks >= 0;
                PowderSnowTicks = StrayConversionTicks = -1;
                if (wasConverting)
                    SynchronizeMetadata();
            }
        }
        if (BurnsInSun && Level.DimensionName == "minecraft:overworld" && Level.DayTime is >= 0 and < 12000 &&
            !Level.LevelData.Raining && !InWater && Random.NextSingle() * 30 < 1.2f && Terrain.GetSkyLight((Vector)EyePosition.Floor()) == 15)
        {
            if (GetEquipment(EquipmentSlot.Helmet).IsAir)
                Ignite(8);
            else
                DamageEquipment(EquipmentSlot.Helmet, Random.Next(2));
        }
    }

    internal void Shoot(IEntity target)
    {
        var origin = EyePosition - new VectorD(0, 0.1f, 0);
        var delta = target.Position + new VectorD(0, target.Dimension.Height / 3, 0) - origin;
        delta.Y += Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z) * 0.2f;
        var direction = delta / delta.Magnitude;
        var deviation = 0.0172275f * (14 - 4 * (int)Level.LevelData.Difficulty);
        direction += new VectorD(Random.NextSingle() - Random.NextSingle(), Random.NextSingle() - Random.NextSingle(), Random.NextSingle() - Random.NextSingle()) * deviation;
        Level.SpawnEntity(new Arrow
        {
            Level = Level, EntityId = Server.GetNextEntityId(), Position = origin, Type = EntityType.Arrow,
            Owner = this, Motion = direction * 1.6f,
            Damage = 2 + MathF.Sqrt(-2 * MathF.Log(Math.Max(float.Epsilon, Random.NextSingle()))) *
                MathF.Cos(2 * MathF.PI * Random.NextSingle()) * 0.25f + (int)Level.LevelData.Difficulty * 0.11f,
            Effect = ArrowEffect, EffectDuration = ArrowEffectDuration,
            FireSeconds = this is WitherSkeleton ? 100 : 0, Burning = this is WitherSkeleton
        });
        if (this is Illusioner)
        {
            if (!Silent)
                PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new Obsidian.Net.Packets.Play.Clientbound.SoundEntityPacket
                { EntityId = EntityId, SoundLocation = "minecraft:entity.skeleton.shoot", Category = SoundCategory.Hostile,
                    Volume = 1, Pitch = 1 / (Random.NextSingle() * 0.4f + 0.8f), Seed = Random.NextInt64() }, EntityId);
        }
        else PlayMobSound("shoot");
    }

    protected override ValueTask OnDeathAsync(IEntity source)
    {
        DropItem(Material.Bone, Random.Next(3));
        DropItem(Material.Arrow, Random.Next(3));
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
            if (!GetEquipment(slot).IsAir && Random.NextSingle() < GetEquipmentDropChance(slot))
                DropItem(GetEquipment(slot));
        return default;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        if (Type == EntityType.Skeleton)
        {
            writer.WriteEntityMetadataType(16, EntityMetadataType.Boolean);
            writer.WriteBoolean(StrayConversionTicks >= 0);
        }
    }
}

internal sealed class SkeletonMeleeGoal(Skeleton skeleton) : MeleeAttackGoal(skeleton, 1.2f)
{
    public override bool CanUse() => skeleton.GetEquipment(EquipmentSlot.MainHand).Type != Material.Bow && base.CanUse();
    public override bool CanContinue() => skeleton.GetEquipment(EquipmentSlot.MainHand).Type != Material.Bow && base.CanContinue();
}

internal sealed class BowAttackGoal(Skeleton skeleton) : NavigationGoal(skeleton, 1)
{
    private int sightTicks;
    private int drawTicks;
    private long nextShot;
    private int strafeTicks = -1;
    private bool clockwise;
    private bool backwards;
    private bool drawing;
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look;
    public override bool RequiresUpdateEveryTick => true;
    public override bool CanUse() => skeleton.GetEquipment(EquipmentSlot.MainHand).Type == Material.Bow &&
        skeleton.AttackTarget is { } target && skeleton.IsValidTarget(target);
    public override bool CanContinue() => CanUse();
    public override void Start() => skeleton.SetAggressive(true);
    public override ValueTask TickAsync()
    {
        var target = skeleton.AttackTarget!;
        var visible = skeleton.CanSee(target);
        sightTicks = visible ? Math.Max(0, sightTicks) + 1 : Math.Min(0, sightTicks) - 1;
        skeleton.LookControl.LookAt(target.Position + new VectorD(0, target.Dimension.Height * 0.85f, 0), 30, 30);
        var distance = (target.Position - skeleton.Position).MagnitudeSquared();
        if (distance <= 225 && sightTicks >= 20)
        {
            Navigation.Stop();
            strafeTicks++;
        }
        else
        {
            MoveTo(target);
            strafeTicks = -1;
        }
        if (strafeTicks >= 20)
        {
            if (skeleton.Random.NextSingle() < 0.3f) clockwise = !clockwise;
            if (skeleton.Random.NextSingle() < 0.3f) backwards = !backwards;
            strafeTicks = 0;
        }
        if (strafeTicks >= 0)
        {
            if (distance > 225 * 0.75f) backwards = false;
            else if (distance < 225 * 0.25f) backwards = true;
            skeleton.Yaw = LookControl.RotateTowards(skeleton.Yaw.Degrees,
                (float)(Math.Atan2(target.Position.Z - skeleton.Position.Z, target.Position.X - skeleton.Position.X) * 180 / Math.PI - 90), 30);
            skeleton.MoveControl.Strafe(backwards ? -0.5f : 0.5f, clockwise ? 0.5f : -0.5f);
        }
        if (!drawing && skeleton.AiTick >= nextShot && sightTicks >= -60)
        {
            drawing = true;
            skeleton.LivingBitMask |= LivingBitMask.HandActive;
            skeleton.SynchronizeMetadata();
        }
        if (drawing)
            drawTicks++;
        if (drawing && visible && drawTicks >= 20)
        {
            skeleton.Shoot(target);
            drawTicks = 0;
            nextShot = skeleton.AiTick + skeleton.AttackInterval;
            drawing = false;
            skeleton.LivingBitMask &= ~LivingBitMask.HandActive;
            skeleton.SynchronizeMetadata();
        }
        if (sightTicks < -60)
        {
            drawTicks = 0;
            drawing = false;
            skeleton.LivingBitMask &= ~LivingBitMask.HandActive;
            skeleton.SynchronizeMetadata();
        }
        return default;
    }
    public override void Stop()
    {
        base.Stop();
        drawTicks = sightTicks = 0;
        drawing = false;
        skeleton.LivingBitMask &= ~LivingBitMask.HandActive;
        skeleton.SetAggressive(false);
    }
}

[MinecraftEntity("minecraft:stray")]
public sealed partial class Stray : Skeleton
{
    public Stray() => Type = EntityType.Stray;
    protected override string? SoundName => "stray";
    internal override int ArrowEffect => 1;
    internal override int ArrowEffectDuration => 600;
}

public sealed class Bogged : Skeleton
{
    public Bogged()
    {
        Type = EntityType.Bogged;
        TryUpdateAttribute("minecraft:generic.max_health", 16);
    }
    public bool Sheared { get; set; }
    protected override string? SoundName => "bogged";
    public override string? TranslationKey { get; protected set; } = "entity.minecraft.bogged";
    internal override int ArrowEffect => 18;
    internal override int ArrowEffectDuration => 100;
    internal override int AttackInterval => Level.LevelData.Difficulty == Difficulty.Hard ? 50 : 70;
    internal override async ValueTask InteractAsync(IPlayer player, InteractionHand hand)
    {
        if (Sheared || !Alive || IsRemoved || player.Level != Level || player.Health <= 0 || player.GameMode == GameMode.Spectator ||
            !IsInRange(player, 4) || !CanSee(player))
            return;
        var held = hand == InteractionHand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        if (held is not { Count: > 0, Type: Material.Shears })
            return;
        Sheared = true;
        for (var index = 0; index < 2; index++)
            DropItem(Random.Next(2) == 0 ? Material.RedMushroom : Material.BrownMushroom);
        await DamageInteractionToolAsync(player, hand);
        SynchronizeMetadata();
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.Boolean);
        writer.WriteBoolean(Sheared);
    }
}

public sealed class Parched : Skeleton
{
    public Parched()
    {
        Type = EntityType.Parched;
        TryUpdateAttribute("minecraft:generic.max_health", 16);
    }
    protected override string? SoundName => "parched";
    public override string? TranslationKey { get; protected set; } = "entity.minecraft.parched";
    protected override bool BurnsInSun => false;
    internal override int ArrowEffect => (int)PotionEffect.Weakness - 1;
    internal override int ArrowEffectDuration => 600;
    internal override int AttackInterval => Level.LevelData.Difficulty == Difficulty.Hard ? 50 : 70;
}

internal sealed class SkeletonShelterGoal(Skeleton skeleton) : NavigationGoal(skeleton, 1)
{
    private VectorD destination;
    public override bool CanUse()
    {
        if (skeleton.Type == EntityType.Parched || !skeleton.Burning || skeleton.Level.DayTime is < 0 or >= 12000 ||
            !skeleton.GetEquipment(EquipmentSlot.Helmet).IsAir || skeleton.Terrain.GetSkyLight((Vector)skeleton.Position.Floor()) < 15)
            return false;
        var evaluator = new WalkNodeEvaluator(skeleton);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var x = (int)Math.Floor(skeleton.Position.X) + skeleton.Random.Next(-10, 10);
            var z = (int)Math.Floor(skeleton.Position.Z) + skeleton.Random.Next(-10, 10);
            if (evaluator.FindGround(x, z, skeleton.Position.Y + skeleton.Random.Next(-3, 3)) is VectorD point &&
                skeleton.Terrain.GetSkyLight((Vector)point.Floor()) < 15)
            {
                destination = point;
                return true;
            }
        }
        return false;
    }
    public override void Start() => MoveTo(destination);
    public override bool CanContinue() => Navigation.IsNavigating;
}
