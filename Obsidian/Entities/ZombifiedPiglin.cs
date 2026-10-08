using Obsidian.API.Inventory;
using Obsidian.Entities.AI;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:zombified_piglin")]
public sealed partial class ZombifiedPiglin : Zombie
{
    internal int AngerTicks { get; set; }
    internal Guid AngryAt { get; set; }
    private long nextAlert;
    private long angrySoundTick;
    public ZombifiedPiglin() => Type = EntityType.ZombifiedPiglin;
    internal override bool Hostile => true;
    protected override string? SoundName => "zombified_piglin";
    internal override float MovementSpeed => base.MovementSpeed + (AngerTicks > 0 && !IsBaby ? 0.05f : 0);

    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(2, new MeleeAttackGoal(this));
        actions.AddGoal(7, new RandomStrollGoal(this, 1));
        actions.AddGoal(8, new LookAtPlayerGoal(this, 8));
        actions.AddGoal(8, new RandomLookAroundGoal(this));
        targets.AddGoal(1, new NearestAttackableTargetGoal(this, target => AngerTicks > 0 && target.Uuid == AngryAt));
    }

    protected override void FinalizeSpawn()
    {
        CanPickUpLoot = Random.NextSingle() < 0.55f * SpecialDifficulty;
        IsBaby |= Random.NextSingle() < 0.05f;
        SetEquipment(EquipmentSlot.MainHand, ItemsRegistry.GetSingleItem(Material.GoldenSword));
    }

    private void BecomeAngry(IEntity source)
    {
        if (!IsValidTarget(source) || source.Type == Type) return;
        if (AngerTicks == 0) angrySoundTick = AiTick + Random.Next(21);
        AngerTicks = Random.Next(400, 781);
        AngryAt = source.Uuid;
        AttackTarget = source;
    }

    protected override ValueTask OnHurtAsync(IEntity source)
    {
        if (!ReferenceEquals(source, this)) BecomeAngry(source);
        return default;
    }

    protected override ValueTask TickMobAsync()
    {
        if (AngerTicks <= 0) return default;
        if (AttackTarget != null && (!IsValidTarget(AttackTarget) || !IsInRange(AttackTarget, FollowRange))) AttackTarget = null;
        if (AttackTarget is not IPlayer && --AngerTicks == 0)
        {
            AngryAt = Guid.Empty;
            AttackTarget = null;
            SetAggressive(false);
            return default;
        }
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return default;
        if (AiTick >= angrySoundTick)
        {
            PlayMobSound("angry");
            angrySoundTick = long.MaxValue;
        }
        if (AttackTarget is { } target && AiTick >= nextAlert)
        {
            nextAlert = AiTick + Random.Next(80, 121);
            foreach (var ally in GetEntitiesNear(FollowRange).OfType<ZombifiedPiglin>().Where(ally => ally.Alive && !ally.IsRemoved &&
                ally.AttackTarget == null && Math.Abs(ally.Position.Y - Position.Y) <= 10 && ally.CanSee(target)))
                ally.BecomeAngry(target);
        }
        return default;
    }

    protected override ValueTask OnDeathAsync(IEntity source)
    {
        DropItem(Material.RottenFlesh, Random.Next(2));
        DropItem(Material.GoldNugget, Random.Next(2));
        if (source is IPlayer && Random.NextSingle() < 0.025f) DropItem(Material.GoldIngot);
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
            if (!GetEquipment(slot).IsAir && Random.NextSingle() < GetEquipmentDropChance(slot)) DropItem(GetEquipment(slot));
        return default;
    }
}
