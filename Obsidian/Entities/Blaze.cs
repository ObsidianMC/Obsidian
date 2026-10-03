using Obsidian.Entities.AI;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:blaze")]
public sealed partial class Blaze : PathfinderMob
{
    internal bool Charged { get; set; }
    private int heightOffsetTicks;
    private float heightOffset;
    public Blaze() => Type = EntityType.Blaze;
    protected override bool UsesAi => true;
    internal override bool Hostile => true;
    protected override bool TakesFallDamage => false;
    protected override bool WaterSensitive => true;
    protected override string? SoundName => "blaze";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override int GetExperienceReward() => 10;
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(4, new BlazeAttackGoal(this));
        actions.AddGoal(5, new RandomStrollGoal(this, 1));
        actions.AddGoal(6, new LookAtPlayerGoal(this, 8));
        actions.AddGoal(7, new RandomLookAroundGoal(this));
        targets.AddGoal(1, new HurtByTargetGoal(this));
        targets.AddGoal(2, new NearestAttackableTargetGoal(this, target => target is IPlayer));
    }
    protected override ValueTask TickMobAsync()
    {
        if (!MobBitMask.HasFlag(MobBitmask.NoAi))
        {
            if (--heightOffsetTicks <= 0)
            {
                heightOffsetTicks = 100;
                heightOffset = 0.5f + Random.NextSingle() * 4;
            }
            if (AttackTarget is { } target && target.Position.Y + target.Dimension.Height * 0.85f > EyePosition.Y + heightOffset)
                Motion = new VectorF(Motion.X, Motion.Y + (0.3f - Motion.Y) * 0.3f, Motion.Z);
        }
        return default;
    }
    protected override VectorF Travel()
    {
        if (Motion.Y < 0)
            Motion = new VectorF(Motion.X, Motion.Y * 0.6f, Motion.Z);
        return base.Travel();
    }
    protected override ValueTask OnDeathAsync(IEntity source)
    {
        if (source is IPlayer)
            DropItem(Material.BlazeRod, Random.Next(2));
        return default;
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.Byte);
        writer.WriteByte((byte)(Charged ? 1 : 0));
    }
}

internal sealed class BlazeAttackGoal(Blaze blaze) : NavigationGoal(blaze, 1)
{
    private int cooldown;
    private int step;
    private int unseenTicks;
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look;
    public override bool RequiresUpdateEveryTick => true;
    public override bool CanUse() => blaze.AttackTarget is { } target && blaze.IsValidTarget(target);
    public override void Start() { step = 0; cooldown = 0; unseenTicks = 0; }
    public override void Stop()
    {
        base.Stop();
        blaze.Charged = false;
        blaze.SynchronizeMetadata();
    }
    public override async ValueTask TickAsync()
    {
        cooldown--;
        var target = blaze.AttackTarget!;
        blaze.LookControl.LookAt(target);
        var visible = blaze.CanSee(target);
        unseenTicks = visible ? 0 : unseenTicks + 1;
        var distance = (target.Position - blaze.Position).MagnitudeSquared();
        if (distance < 4)
        {
            if (visible && cooldown <= 0)
            {
                cooldown = 20;
                await blaze.PerformMeleeAttackAsync(target);
            }
            MoveTo(target);
            return;
        }
        if (distance < blaze.FollowRange * blaze.FollowRange && visible)
        {
            Navigation.Stop();
            if (cooldown > 0)
                return;
            step++;
            if (step == 1)
            {
                cooldown = 60;
                blaze.Charged = true;
                blaze.SynchronizeMetadata();
            }
            else if (step <= 4)
            {
                cooldown = 6;
                var spread = MathF.Sqrt(MathF.Sqrt(distance)) * 0.5f;
                var direction = target.Position + new VectorF(0, target.Dimension.Height * 0.5f, 0) - blaze.EyePosition;
                direction += new VectorF((blaze.Random.NextSingle() - blaze.Random.NextSingle()) * spread, 0,
                    (blaze.Random.NextSingle() - blaze.Random.NextSingle()) * spread);
                blaze.PlayMobSound("shoot");
                blaze.Level.SpawnEntity(new MobProjectile(blaze, EntityType.SmallFireball, blaze.EyePosition, direction));
            }
            else
            {
                cooldown = 100;
                step = 0;
                blaze.Charged = false;
                blaze.SynchronizeMetadata();
            }
        }
        else if (unseenTicks < 5)
            MoveTo(target);
    }
}
