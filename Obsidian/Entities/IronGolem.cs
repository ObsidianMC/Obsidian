using Obsidian.Entities.AI;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:iron_golem")]
public sealed partial class IronGolem : PathfinderMob
{
    public IronGolem()
    {
        Type = EntityType.IronGolem;
        PersistenceRequired = true;
    }

    public bool PlayerCreated { get; set; }
    protected override bool UsesAi => true;
    protected override string? SoundName => "iron_golem";
    protected internal override float AttackDamage => base.AttackDamage / 2 + Random.Next((int)base.AttackDamage);

    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(1, new MeleeAttackGoal(this));
        actions.AddGoal(6, new RandomStrollGoal(this, 0.6f));
        actions.AddGoal(7, new LookAtPlayerGoal(this, 6));
        actions.AddGoal(8, new RandomLookAroundGoal(this));
        targets.AddGoal(1, new GolemDefendGoal(this));
        targets.AddGoal(3, new NearestAttackableTargetGoal(this, IsHostile));
    }

    internal static bool IsHostile(IEntity entity) => entity.Type is EntityType.Zombie or EntityType.Husk or
        EntityType.Drowned or EntityType.ZombieVillager or EntityType.Skeleton or EntityType.Stray or EntityType.Bogged or
        EntityType.Parched or EntityType.WitherSkeleton or EntityType.Spider or EntityType.CaveSpider or EntityType.Pillager or
        EntityType.Vindicator or EntityType.Evoker or EntityType.Witch or EntityType.Ravager or EntityType.Silverfish or
        EntityType.Endermite or EntityType.Blaze or EntityType.Slime or EntityType.MagmaCube;

    protected internal override async ValueTask PerformMeleeAttackAsync(IEntity target)
    {
        SendEntityEvent(4);
        PlayMobSound("attack");
        await target.DamageAsync(this, AttackDamage);
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.Byte);
        writer.WriteByte(PlayerCreated ? (byte)1 : (byte)0);
    }
}

internal sealed class GolemDefendGoal(IronGolem golem) : Goal
{
    private IEntity? candidate;
    private long handledHurtTick = -100;
    public override GoalFlags Flags => GoalFlags.Target;
    private bool CanAttack(IEntity? entity) => entity != null && golem.IsValidTarget(entity) &&
        entity.Type != EntityType.Creeper && entity.Type != EntityType.IronGolem &&
        entity.Type != EntityType.Villager && !(golem.PlayerCreated && entity is IPlayer);

    public override bool CanUse()
    {
        candidate = golem.LastHurtTick != handledHurtTick && golem.AiTick - golem.LastHurtTick <= 100 ? golem.LastAttacker : null;
        if (CanAttack(candidate))
            return true;
        if (golem.Random.Next(5) != 0)
            return false;
        candidate = golem.GetEntitiesNear(golem.FollowRange).OfType<Villager>()
            .Where(villager => villager.Alive && !villager.IsRemoved && villager.AiTick - villager.LastHurtTick <= 100)
            .Select(villager => villager.LastAttacker)
            .Where(attacker => CanAttack(attacker) && golem.IsInRange(attacker!, golem.FollowRange))
            .MinBy(attacker => (attacker!.Position - golem.Position).MagnitudeSquared());
        return CanAttack(candidate);
    }
    public override void Start()
    {
        handledHurtTick = golem.LastHurtTick;
        golem.AttackTarget = candidate;
    }
    public override bool CanContinue() => CanAttack(golem.AttackTarget) && golem.IsInRange(golem.AttackTarget!, golem.FollowRange);
    public override void Stop()
    {
        golem.AttackTarget = null;
        candidate = null;
    }
}
