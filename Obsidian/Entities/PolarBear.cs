using Obsidian.Entities.AI;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:polar_bear")]
public sealed partial class PolarBear : Animal
{
    private bool standing;
    private long nextWarning;
    internal int AngerTicks { get; set; }
    internal Guid AngryAt { get; set; }
    public PolarBear() => Type = EntityType.PolarBear;
    protected override bool UsesAi => true;
    protected override string? SoundName => "polar_bear";
    internal override bool CanBreed => false;
    internal override bool PanicsWhenHurt => IsBaby;
    protected override int GetExperienceReward() => IsBaby ? 0 : Random.Next(1, 4);

    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(0, new FloatGoal(this));
        actions.AddGoal(1, new PolarBearMeleeGoal(this));
        actions.AddGoal(1, new PanicGoal(this, 2));
        actions.AddGoal(4, new FollowParentGoal(this));
        actions.AddGoal(5, new RandomStrollGoal(this, 1));
        actions.AddGoal(6, new LookAtPlayerGoal(this, 6));
        actions.AddGoal(7, new RandomLookAroundGoal(this));
        targets.AddGoal(1, new NearestAttackableTargetGoal(this, entity => !IsBaby && AngerTicks > 0 && entity.Uuid == AngryAt));
        targets.AddGoal(2, new NearestAttackableTargetGoal(this, entity => !IsBaby && entity is IPlayer &&
            Level.LevelData.Difficulty != Difficulty.Peaceful && IsInRange(entity, FollowRange / 2) &&
            GetEntitiesNear(8).OfType<PolarBear>().Any(bear => bear.IsBaby && Math.Abs(bear.Position.Y - Position.Y) <= 4)));
        targets.AddGoal(3, new NearestAttackableTargetGoal(this, entity => !IsBaby && entity.Type == EntityType.Fox));
    }

    protected override ValueTask OnHurtAsync(IEntity source)
    {
        if (IsValidTarget(source))
        {
            foreach (var bear in GetEntitiesNear(FollowRange).OfType<PolarBear>().Append(this).Where(bear => !bear.IsBaby))
            {
                bear.AngryAt = source.Uuid;
                bear.AngerTicks = Random.Next(400, 800);
                bear.AttackTarget = source;
            }
        }
        return base.OnHurtAsync(source);
    }

    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (AngerTicks > 0 && AttackTarget is not IPlayer && --AngerTicks == 0)
        {
            AngryAt = Guid.Empty;
            AttackTarget = null;
        }
        if (Level.LevelData.Difficulty == Difficulty.Peaceful && AttackTarget is IPlayer)
        {
            AttackTarget = null;
            AngryAt = Guid.Empty;
            AngerTicks = 0;
            SetStanding(false);
        }
    }

    internal void SetStanding(bool value)
    {
        if (standing != value) { standing = value; SynchronizeMetadata(); }
        if (value && AiTick >= nextWarning)
        {
            nextWarning = AiTick + 40;
            PlayMobSound("warning");
        }
    }

    protected override ValueTask OnDeathAsync(IEntity source)
    {
        if (!IsBaby)
        {
            var cod = Random.Next(4) != 0;
            var fish = cod ? Material.Cod : Material.Salmon;
            if (Burning) fish = cod ? Material.CookedCod : Material.CookedSalmon;
            DropItem(fish, Random.Next(3));
        }
        return default;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(17, EntityMetadataType.Boolean);
        writer.WriteBoolean(standing);
    }
}

internal sealed class PolarBearMeleeGoal(PolarBear bear) : MeleeAttackGoal(bear, 1.25f)
{
    public override bool CanUse() => !bear.IsBaby && base.CanUse();
    public override bool CanContinue() => !bear.IsBaby && base.CanContinue();
    public override async ValueTask TickAsync()
    {
        await base.TickAsync();
        bear.SetStanding(bear.AttackTarget is { } target && !bear.IsWithinMeleeRange(target) &&
            (target.Position - bear.Position).MagnitudeSquared() < Math.Pow(target.Dimension.Width + 3, 2) && AttackCooldown <= 10);
    }
    public override void Stop() { bear.SetStanding(false); base.Stop(); }
}
