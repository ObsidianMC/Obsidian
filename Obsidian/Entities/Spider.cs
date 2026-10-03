using Obsidian.Entities.AI;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:spider")]
public partial class Spider : PathfinderMob
{
    private bool climbing;
    public Spider() => Type = EntityType.Spider;
    protected override bool UsesAi => true;
    internal override bool Hostile => true;
    protected override string? SoundName => "spider";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override int GetExperienceReward() => 5;
    private bool Dark => Math.Max(Terrain.GetBlockLight((Vector)Position.Floor()),
        Math.Max(0, Terrain.GetSkyLight((Vector)Position.Floor()) - (Level.DayTime is >= 12000 and < 23000 ? 11 : 0))) < 8;
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(1, new FloatGoal(this));
        actions.AddGoal(2, new SpiderLeapGoal(this));
        actions.AddGoal(3, new SpiderAttackGoal(this));
        actions.AddGoal(5, new RandomStrollGoal(this, 0.8f));
        actions.AddGoal(6, new LookAtPlayerGoal(this, 8));
        actions.AddGoal(7, new RandomLookAroundGoal(this));
        targets.AddGoal(1, new HurtByTargetGoal(this));
        targets.AddGoal(2, new NearestAttackableTargetGoal(this, entity => Dark && (entity is IPlayer || entity.Type == EntityType.IronGolem)));
    }
    protected override ValueTask TickMobAsync()
    {
        var value = MovementFlags.HasFlag(MovementFlags.HorizontalCollision);
        if (climbing != value)
        {
            climbing = value;
            SynchronizeMetadata();
        }
        if (!Dark && AttackTarget != null && AiTick - LastHurtTick > 100 && Random.Next(100) == 0)
        {
            TargetGoals.Cancel();
            AttackTarget = null;
        }
        return default;
    }
    protected override VectorF Travel()
    {
        if (climbing && !MobBitMask.HasFlag(MobBitmask.NoAi))
            Motion = new VectorF(Motion.X, 0.2f, Motion.Z);
        return base.Travel();
    }
    protected override ValueTask OnDeathAsync(IEntity source)
    {
        DropItem(Material.String, Random.Next(3));
        if (source is IPlayer && Random.Next(3) == 0)
            DropItem(Material.SpiderEye, 1);
        return default;
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.Byte);
        writer.WriteByte((byte)(climbing ? 1 : 0));
    }
}

internal sealed class SpiderLeapGoal(Spider spider) : Goal
{
    private int ticks;
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Jump;
    public override bool CanUse() => spider.AttackTarget is { } target && spider.IsValidTarget(target) &&
        spider.MovementFlags.HasFlag(MovementFlags.OnGround) &&
        (target.Position - spider.Position).MagnitudeSquared() is >= 4 and <= 16 && spider.Random.Next(5) == 0;
    public override bool CanContinue() => --ticks > 0 && !spider.MovementFlags.HasFlag(MovementFlags.OnGround);
    public override void Start()
    {
        ticks = 20;
        var direction = spider.AttackTarget!.Position - spider.Position;
        direction.Y = 0;
        if (direction.Magnitude > 0.001f)
            direction /= direction.Magnitude;
        spider.Motion = direction * 0.4f + new VectorF(spider.Motion.X * 0.2f, 0.4f, spider.Motion.Z * 0.2f);
        ((Navigator)spider.Navigator!).Stop();
    }
}

internal sealed class SpiderAttackGoal(Spider spider) : MeleeAttackGoal(spider)
{
    public override bool CanUse() => spider.AttackTarget is { } target && spider.IsValidTarget(target);
    public override bool CanContinue() => CanUse();
    public override async ValueTask TickAsync()
    {
        await base.TickAsync();
        if (spider.AttackTarget is { } target && !((Navigator)spider.Navigator!).IsNavigating && !spider.IsWithinMeleeRange(target))
            spider.MoveControl.MoveTo(target.Position, 1);
    }
}
