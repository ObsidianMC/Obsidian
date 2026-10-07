using Obsidian.API.Inventory;
using Obsidian.Entities.AI;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:drowned")]
public sealed partial class Drowned : Zombie
{
    public Drowned() => Type = EntityType.Drowned;
    internal override bool Hostile => true;
    internal override bool SwimmingNavigation => true;
    protected override string? SoundName => "drowned";
    protected override VectorD Travel() => VolumeMovement.Travel(this, InWater);
    protected internal override float GetPathCost(IBlock feet, IBlock floor) => feet.Material == Material.Water ? 0 : base.GetPathCost(feet, floor);
    internal bool CanAttackNow(IEntity target) => Level.DayTime is < 0 or >= 12000 ||
        Terrain.GetBlock((Vector)(target.Position + new VectorD(0, 0.1f, 0)).Floor())?.Material == Material.Water;

    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(1, new SeekWaterGoal(this, onlyDuringDay: true));
        actions.AddGoal(2, new DrownedTridentGoal(this));
        actions.AddGoal(2, new DrownedMeleeGoal(this));
        actions.AddGoal(7, new RandomStrollGoal(this, 1));
        actions.AddGoal(8, new LookAtPlayerGoal(this, 8));
        actions.AddGoal(8, new RandomLookAroundGoal(this));
        targets.AddGoal(1, new HurtByTargetGoal(this));
        targets.AddGoal(2, new NearestAttackableTargetGoal(this, target => target is IPlayer && CanAttackNow(target)));
        targets.AddGoal(3, new NearestAttackableTargetGoal(this, target => target.Type is EntityType.Villager or EntityType.IronGolem or EntityType.Axolotl));
        targets.AddGoal(5, new NearestAttackableTargetGoal(this, target => target.Type == EntityType.Turtle &&
            target is AgeableMob { IsBaby: true } && target.MovementFlags.HasFlag(MovementFlags.OnGround)));
    }

    protected override void FinalizeSpawn()
    {
        CanPickUpLoot = Random.NextSingle() < 0.55f * SpecialDifficulty;
        IsBaby |= Random.NextSingle() < 0.05f;
        if (Random.NextSingle() > 0.9f)
            SetEquipment(EquipmentSlot.MainHand, ItemsRegistry.GetSingleItem(Random.Next(16) < 10 ? Material.Trident : Material.FishingRod));
        if (Random.NextSingle() < 0.03f)
        {
            SetEquipment(EquipmentSlot.OffHand, ItemsRegistry.GetSingleItem(Material.NautilusShell));
            SetEquipmentDropChance(EquipmentSlot.OffHand, 2);
        }
    }

    protected override ValueTask TickAirSupplyAsync() { Air = 300; return default; }
    protected override ValueTask OnHurtAsync(IEntity source) => default;
    protected override ValueTask OnDeathAsync(IEntity source)
    {
        DropItem(Material.RottenFlesh, Random.Next(3));
        if (source is IPlayer && Random.NextSingle() < 0.11f) DropItem(Material.CopperIngot);
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
            if (!GetEquipment(slot).IsAir && Random.NextSingle() < GetEquipmentDropChance(slot)) DropItem(GetEquipment(slot));
        return default;
    }
}

internal sealed class DrownedMeleeGoal(Drowned drowned) : MeleeAttackGoal(drowned)
{
    public override bool CanUse() => drowned.GetEquipment(EquipmentSlot.MainHand).Type != Material.Trident &&
        drowned.AttackTarget is { } target && drowned.CanAttackNow(target) && base.CanUse();
    public override bool CanContinue() => drowned.GetEquipment(EquipmentSlot.MainHand).Type != Material.Trident &&
        drowned.AttackTarget is { } target && drowned.CanAttackNow(target) && base.CanContinue();
}

internal sealed class DrownedTridentGoal(Drowned drowned) : NavigationGoal(drowned, 1)
{
    private int sightTicks;
    private long nextThrow;
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look;
    public override bool RequiresUpdateEveryTick => true;
    public override bool CanUse() => drowned.GetEquipment(EquipmentSlot.MainHand).Type == Material.Trident &&
        drowned.AttackTarget is { } target && drowned.IsValidTarget(target) && drowned.CanAttackNow(target);
    public override bool CanContinue() => CanUse();
    public override void Start()
    {
        sightTicks = 0;
        nextThrow = drowned.AiTick + 40;
        drowned.LivingBitMask |= LivingBitMask.HandActive;
        drowned.SetAggressive(true);
        drowned.SynchronizeMetadata();
    }
    public override ValueTask TickAsync()
    {
        var target = drowned.AttackTarget!;
        var visible = drowned.CanSee(target);
        sightTicks = visible ? sightTicks + 1 : 0;
        drowned.LookControl.LookAt(target);
        if (!drowned.IsInRange(target, 10) || sightTicks < 5) MoveTo(target);
        else Navigation.Stop();
        if (drowned.AiTick < nextThrow || !visible || !drowned.IsInRange(target, 10)) return default;
        nextThrow = drowned.AiTick + 40;
        var origin = drowned.EyePosition - new VectorD(0, 0.1f, 0);
        var delta = target.Position + new VectorD(0, target.Dimension.Height / 3, 0) - origin;
        delta.Y += Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z) * 0.2f;
        if (delta.Magnitude < 0.0001f) return default;
        var deviation = 0.0172275f * (14 - 4 * (int)drowned.Level.LevelData.Difficulty);
        var direction = delta / delta.Magnitude + new VectorD(drowned.Random.NextSingle() - drowned.Random.NextSingle(),
            drowned.Random.NextSingle() - drowned.Random.NextSingle(), drowned.Random.NextSingle() - drowned.Random.NextSingle()) * deviation;
        drowned.Level.SpawnEntity(new Trident { Level = drowned.Level, Type = EntityType.Trident, EntityId = Server.GetNextEntityId(),
            Position = origin, Motion = direction * 1.6f, Owner = drowned });
        drowned.PlayMobSound("shoot");
        return default;
    }
    public override void Stop()
    {
        base.Stop();
        drowned.LivingBitMask &= ~LivingBitMask.HandActive;
        drowned.SetAggressive(false);
        drowned.SynchronizeMetadata();
    }
}
