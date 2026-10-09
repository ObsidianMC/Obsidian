using Obsidian.Entities.AI;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:breeze")]
public sealed partial class Breeze : PathfinderMob
{
    public Breeze() => Type = EntityType.Breeze;
    protected override bool UsesAi => true;
    internal override bool Hostile => true;
    internal override bool PanicsWhenHurt => false;
    protected override string? SoundName => "breeze";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override bool CanTakeDamage(IEntity source) => source is not Breeze;
    protected override int GetExperienceReward() => 10;
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(0, new FloatGoal(this));
        actions.AddGoal(1, new BreezeAttackGoal(this));
        actions.AddGoal(6, new RandomStrollGoal(this, 0.6f));
        actions.AddGoal(8, new LookAtPlayerGoal(this, 8));
        actions.AddGoal(9, new RandomLookAroundGoal(this));
        targets.AddGoal(1, new HurtByTargetGoal(this));
        targets.AddGoal(2, new NearestAttackableTargetGoal(this, entity => entity is IPlayer || entity.Type == EntityType.IronGolem));
    }
    protected override async ValueTask TickMobAsync()
    {
        if (Level.LevelData.Difficulty == Difficulty.Peaceful) await RemoveAsync();
    }
    protected override ValueTask OnDeathAsync(IEntity source)
    {
        if (source is IPlayer) DropItem(Material.BreezeRod, Random.Next(1, 3));
        return default;
    }
    internal void SetAttackPose(Pose pose)
    {
        if (Pose == pose) return;
        Pose = pose;
        SynchronizeMetadata();
    }
}

internal sealed class BreezeAttackGoal(Breeze breeze) : NavigationGoal(breeze, 1)
{
    private int phaseTicks;
    private int shootCooldown;
    private int jumpCooldown;
    private VectorD jumpVelocity;
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look | GoalFlags.Jump;
    public override bool RequiresUpdateEveryTick => true;
    public override bool CanUse() => breeze.AttackTarget is { } target && breeze.IsValidTarget(target);
    public override void Start() { phaseTicks = 0; shootCooldown = 0; jumpCooldown = 10; }
    public override void Stop() { base.Stop(); breeze.SetAttackPose(Pose.Standing); }
    public override ValueTask TickAsync()
    {
        if (breeze.AttackTarget is not { } target) return default;
        breeze.LookControl.LookAt(target);
        shootCooldown--;
        jumpCooldown--;
        if (breeze.Pose == Pose.LongJumper)
        {
            if (++phaseTicks > 2 && breeze.MovementFlags.HasFlag(MovementFlags.OnGround))
            { breeze.SetAttackPose(Pose.Standing); jumpCooldown = 10; shootCooldown = 0; phaseTicks = 0; }
            return default;
        }
        if (breeze.Pose == Pose.Inhaling)
        {
            if (++phaseTicks >= 10)
            { breeze.Motion = jumpVelocity; breeze.SetAttackPose(Pose.LongJumper); phaseTicks = 0; breeze.PlayMobSound("jump"); }
            return default;
        }
        if (breeze.Pose == Pose.Shooting)
        {
            if (++phaseTicks == 15 && breeze.CanSee(target))
            {
                var origin = breeze.Position + new VectorD(0, breeze.Dimension.Height * 0.5f + 0.3f, 0);
                var direction = target.Position + new VectorD(0, target.Dimension.Height * 0.5f, 0) - origin;
                var spread = (5 - (int)breeze.Level.LevelData.Difficulty * 4) * 0.0075f;
                if (direction.Magnitude > 0.001f) direction /= direction.Magnitude;
                direction += new VectorD((breeze.Random.NextSingle() - breeze.Random.NextSingle()) * spread,
                    (breeze.Random.NextSingle() - breeze.Random.NextSingle()) * spread,
                    (breeze.Random.NextSingle() - breeze.Random.NextSingle()) * spread);
                breeze.Level.SpawnEntity(new BreezeWindCharge(breeze, origin, direction));
                breeze.PlayMobSound("shoot");
            }
            if (phaseTicks >= 19) { breeze.SetAttackPose(Pose.Standing); phaseTicks = 0; shootCooldown = 10; }
            return default;
        }
        var distance = (target.Position - breeze.Position).Magnitude;
        if (jumpCooldown <= 0 && breeze.MovementFlags.HasFlag(MovementFlags.OnGround) && !breeze.InWater && !breeze.InLava && distance > 4)
        {
            var angle = breeze.Random.NextSingle() * MathF.Tau;
            var desired = target.Position + new VectorD(MathF.Cos(angle) * 6, 0, MathF.Sin(angle) * 6);
            var evaluator = new WalkNodeEvaluator(breeze);
            if (evaluator.FindGround((int)Math.Floor(desired.X), (int)Math.Floor(desired.Z), desired.Y + 3) is VectorD destination &&
                breeze.Terrain.IsFree(breeze.Dimension.CreateBBFromPosition(destination)) &&
                breeze.Terrain.HasLineOfSight(breeze.EyePosition, destination + new VectorD(0, breeze.EyeHeight, 0)))
            {
                var delta = destination - breeze.Position;
                var horizontal = Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
                if (horizontal is > 1 and <= 16)
                {
                    var flightTicks = Math.Clamp(horizontal / 0.7, 8, 20);
                    jumpVelocity = new VectorD(delta.X / flightTicks, delta.Y / flightTicks + 0.04 * flightTicks, delta.Z / flightTicks);
                    Navigation.Stop(); breeze.SetAttackPose(Pose.Inhaling); phaseTicks = 0;
                    breeze.PlayMobSound("inhale"); return default;
                }
            }
            jumpCooldown = 20;
        }
        if (distance <= 16 && breeze.CanSee(target) && shootCooldown <= 0)
        {
            Navigation.Stop(); breeze.SetAttackPose(Pose.Shooting); phaseTicks = 0;
        }
        else if (distance > 16 || !breeze.CanSee(target))
        { breeze.SetAttackPose(Pose.Sliding); MoveTo(target); }
        return default;
    }
}
