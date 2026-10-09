using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:vex")]
public sealed partial class Vex : PathfinderMob
{
    internal bool Charging { get; set; }
    internal Guid OwnerUuid { get; set; }
    public Vector? BoundPosition { get; set; }
    public int? LifeTicks { get; set; }
    public Vex() => Type = EntityType.Vex;
    protected override bool UsesAi => true;
    internal override bool Hostile => true;
    internal override bool FlyingNavigation => true;
    protected override bool TakesFallDamage => false;
    protected override string? SoundName => "vex";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override int GetExperienceReward() => 3;
    internal override float MovementSpeed => 0.3f;
    protected internal override float AttackDamage => base.AttackDamage + GetWeaponDamage(GetEquipment(EquipmentSlot.MainHand));
    protected override void FinalizeSpawn()
    {
        SetEquipment(EquipmentSlot.MainHand, ItemsRegistry.GetSingleItem(Material.IronSword));
        SetEquipmentDropChance(EquipmentSlot.MainHand, 0);
        BoundPosition ??= (Vector)Position.Floor();
    }
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(4, new VexChargeGoal(this));
        actions.AddGoal(8, new VexWanderGoal(this));
        actions.AddGoal(9, new LookAtPlayerGoal(this, 3));
        actions.AddGoal(10, new RandomLookAroundGoal(this));
        targets.AddGoal(1, new HurtByTargetGoal(this));
        targets.AddGoal(2, new NearestAttackableTargetGoal(this, target => target is IPlayer));
    }
    protected override async ValueTask TickMobAsync()
    {
        if (LifeTicks is int life)
        {
            LifeTicks = life - 1;
            if (LifeTicks <= 0) { LifeTicks = 20; await DamageEnvironmentAsync(1); }
        }
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return;
        if (OwnerUuid != Guid.Empty && AiTick % 20 == 0 &&
            Level.GetEntitiesInRange(Position, 64).OfType<Mob>().FirstOrDefault(entity => entity.Uuid == OwnerUuid) is { AttackTarget: { } target } && IsValidTarget(target))
            AttackTarget = target;
    }
    protected override VectorD Travel()
    {
        MovementFlags &= ~MovementFlags.OnGround;
        Motion += MoveControl.Acceleration;
        var position = Position + Motion;
        Motion *= 0.91;
        return position;
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.Byte);
        writer.WriteByte((byte)(Charging ? 1 : 0));
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        if (LifeTicks is int life) writer.WriteInt("life_ticks", life);
        if (BoundPosition is Vector bound) writer.WriteArray("bound_pos", new[] { bound.X, bound.Y, bound.Z });
        if (OwnerUuid != Guid.Empty) writer.WriteArray("owner", EntityNbt.UuidToInts(OwnerUuid));
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        LifeTicks = tag.TryGetTagValue<int>("life_ticks", out var life) ? Math.Max(0, life) : null;
        if (tag.TryGetTag<NbtArray<int>>("bound_pos", out var bound) && bound.Count == 3)
        {
            var coordinates = bound.GetArray();
            BoundPosition = new Vector(coordinates[0], coordinates[1], coordinates[2]);
        }
        if (tag.TryGetTag<NbtArray<int>>("owner", out var owner) && owner.Count == 4) OwnerUuid = EntityNbt.UuidFromInts(owner.GetArray());
    }
}

internal sealed class VexChargeGoal(Vex vex) : Goal
{
    private int ticks;
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look;
    public override bool RequiresUpdateEveryTick => true;
    public override bool CanUse() => vex.AttackTarget is { } target && vex.IsValidTarget(target) && vex.Random.Next(7) == 0;
    public override bool CanContinue() => ticks > 0 && vex.AttackTarget is { } target && vex.IsValidTarget(target);
    public override void Start()
    {
        ticks = 60;
        vex.Charging = true;
        vex.SynchronizeMetadata();
        vex.PlayMobSound("charge");
    }
    public override async ValueTask TickAsync()
    {
        ticks--;
        var target = vex.AttackTarget!;
        vex.LookControl.LookAt(target);
        vex.MoveControl.MoveTo(target.Position + new VectorD(0, target.Dimension.Height / 2, 0), 1);
        if (vex.IsWithinMeleeRange(target))
        {
            await vex.PerformMeleeAttackAsync(target);
            ticks = 0;
        }
    }
    public override void Stop()
    {
        vex.MoveControl.Stop();
        vex.Charging = false;
        vex.SynchronizeMetadata();
    }
}

internal sealed class VexWanderGoal(Vex vex) : Goal
{
    private VectorD destination;
    private int ticks;
    public override GoalFlags Flags => GoalFlags.Move;
    public override bool RequiresUpdateEveryTick => true;
    public override bool CanUse()
    {
        if (vex.Random.Next(7) != 0) return false;
        var origin = vex.BoundPosition ?? (Vector)vex.Position.Floor();
        destination = new VectorD(origin.X + vex.Random.Next(-7, 8) + 0.5, origin.Y + vex.Random.Next(-5, 6) + 0.5,
            origin.Z + vex.Random.Next(-7, 8) + 0.5);
        return vex.Terrain.IsFree(vex.Dimension.CreateBBFromPosition(destination));
    }
    public override void Start() => ticks = 60;
    public override bool CanContinue() => ticks > 0 && (destination - vex.Position).MagnitudeSquared() > 1;
    public override ValueTask TickAsync() { ticks--; vex.MoveControl.MoveTo(destination, 0.25f); return default; }
    public override void Stop() => vex.MoveControl.Stop();
}
