using Obsidian.Entities.AI;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:slime")]
public sealed partial class Slime : PathfinderMob
{
    private int size = 1;
    private long nextAttack;
    public Slime()
    {
        Type = EntityType.Slime;
        MoveControl = new SlimeMoveControl(this);
    }
    public int Size
    {
        get => size;
        set
        {
            size = Math.Clamp(value, 1, 127);
            TryUpdateAttribute("minecraft:generic.max_health", size * size);
            TryUpdateAttribute("minecraft:generic.movement_speed", 0.2f + 0.1f * size);
            TryUpdateAttribute("minecraft:generic.attack_damage", size);
        }
    }
    protected override bool UsesAi => true;
    protected override float DimensionScale => Size * 0.52f / 2.04f;
    protected override string? SoundName => "slime";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override int GetExperienceReward() => Size;
    protected override void FinalizeSpawn()
    {
        var exponent = Random.Next(3);
        if (exponent < 2 && Random.NextSingle() < 0.5f * SpecialDifficulty)
            exponent++;
        Size = 1 << exponent;
        Health = Size * Size;
    }
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(1, new FloatGoal(this));
        targets.AddGoal(1, new NearestAttackableTargetGoal(this, entity => entity is IPlayer && MathF.Abs(entity.Position.Y - Position.Y) <= 4));
        targets.AddGoal(3, new NearestAttackableTargetGoal(this, entity => entity.Type == EntityType.IronGolem));
    }
    protected override async ValueTask TickMobAsync()
    {
        if (Level.LevelData.Difficulty == Difficulty.Peaceful)
        {
            await RemoveAsync();
            return;
        }
        if (Size > 1 && AiTick >= nextAttack && AttackTarget is { } target && IsValidTarget(target) && CanSee(target) &&
            (target.Position - Position).MagnitudeSquared() < MathF.Pow(0.6f * Size, 2) &&
            MobTerrain.Overlaps(Dimension.CreateBBFromPosition(Position), target.Dimension.CreateBBFromPosition(target.Position)))
        {
            nextAttack = AiTick + 10;
            await PerformMeleeAttackAsync(target);
        }
    }
    protected override ValueTask OnDeathAsync(IEntity source)
    {
        if (Size == 1)
            DropItem(Material.SlimeBall, Random.Next(3));
        else
        {
            var count = Random.Next(2, 5);
            for (var index = 0; index < count; index++)
            {
                var position = Position + new VectorF((index % 2 - 0.5f) * Size / 4, 0.5f, (index / 2 - 0.5f) * Size / 4);
                var child = new Slime { Level = Level, EntityId = Server.GetNextEntityId(), Position = position };
                child.InitializeAi(false);
                child.Size = Size / 2;
                child.Health = child.Size * child.Size;
                child.CustomName = CustomName;
                child.PersistenceRequired = PersistenceRequired;
                Level.SpawnEntity(child);
            }
        }
        return default;
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.VarInt);
        writer.WriteVarInt(Size);
    }
}

internal sealed class SlimeMoveControl(Slime slime) : MoveControl(slime)
{
    private int jumpDelay;
    private int turnDelay;
    internal override void Tick()
    {
        Stop();
        if (slime.AttackTarget is { } target)
            slime.Yaw = LookControl.RotateTowards(slime.Yaw.Degrees,
                MathF.Atan2(target.Position.Z - slime.Position.Z, target.Position.X - slime.Position.X) * 180 / MathF.PI - 90, 10);
        else if (--turnDelay <= 0)
        {
            turnDelay = slime.Random.Next(40, 100);
            slime.Yaw = slime.Random.Next(360);
        }
        if (slime.MovementFlags.HasFlag(MovementFlags.OnGround))
        {
            if (--jumpDelay > 0)
                return;
            jumpDelay = slime.Random.Next(10, 30);
            if (slime.AttackTarget != null)
                jumpDelay /= 3;
            slime.JumpControl.Jump();
            slime.PlayMobSound("jump");
        }
        Ride(slime.MovementSpeed);
    }
}
