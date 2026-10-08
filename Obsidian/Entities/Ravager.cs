using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:ravager")]
public sealed partial class Ravager : PathfinderMob
{
    private int attackTicks;
    private int stunTicks;
    private int roarTicks;
    private float movementSpeed = 0.3f;
    public Ravager() => Type = EntityType.Ravager;
    protected override bool UsesAi => true;
    internal override bool Hostile => true;
    protected override string? SoundName => "ravager";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override int GetExperienceReward() => 20;
    internal override float MovementSpeed => attackTicks > 0 || stunTicks > 0 || roarTicks > 0 ? 0 : movementSpeed;
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(0, new FloatGoal(this));
        actions.AddGoal(4, new RavagerMeleeGoal(this));
        actions.AddGoal(5, new RandomStrollGoal(this, 0.4f));
        actions.AddGoal(6, new LookAtPlayerGoal(this, 6));
        actions.AddGoal(10, new RandomLookAroundGoal(this));
        targets.AddGoal(1, new HurtByTargetGoal(this));
        targets.AddGoal(2, new NearestAttackableTargetGoal(this, target => target is IPlayer));
        targets.AddGoal(3, new NearestAttackableTargetGoal(this, target => target.Type is EntityType.Villager or EntityType.IronGolem or EntityType.WanderingTrader));
    }
    internal bool CanAttack => stunTicks == 0 && roarTicks == 0;
    protected override async ValueTask TickMobAsync()
    {
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return;
        movementSpeed += ((AttackTarget == null ? 0.3f : 0.35f) - movementSpeed) * 0.1f;
        if (attackTicks > 0) attackTicks--;
        if (stunTicks > 0 && --stunTicks == 0)
        {
            roarTicks = 20;
            SendEntityEvent(69);
            PlayMobSound("roar");
        }
        if (roarTicks > 0 && --roarTicks == 10)
        {
            foreach (var target in GetEntitiesNear(4 + Dimension.Width).OfType<Living>().Where(target =>
                target.Health > 0 && target.Type != EntityType.Ravager && target is not IPlayer { GameMode: GameMode.Creative or GameMode.Spectator }))
            {
                if (target.Type is not (EntityType.Pillager or EntityType.Vindicator or EntityType.Evoker or EntityType.Illusioner))
                    await target.DamageAsync(this, 6);
                if (target is not IPlayer) PushAway(target, 4);
            }
        }
        if (AiTick % 5 != 0 || Motion.MagnitudeSquared() < 0.0001) return;
        var bounds = Dimension.CreateBBFromPosition(Position + new VectorD(Motion.X, 0, Motion.Z));
        if (Terrain.IsFree(bounds)) return;
        for (var x = (int)Math.Floor(bounds.Min.X); x <= (int)Math.Floor(bounds.Max.X); x++)
        for (var y = (int)Math.Floor(bounds.Min.Y); y <= (int)Math.Floor(bounds.Max.Y); y++)
        for (var z = (int)Math.Floor(bounds.Min.Z); z <= (int)Math.Floor(bounds.Max.Z); z++)
        {
            var point = new Vector(x, y, z);
            if (Terrain.GetBlock(point) is { } block && TagsRegistry.Block.Leaves.Entries.Contains(block.RegistryId))
                await Level.SetBlockAsync(point, BlocksRegistry.Air, true);
        }
    }
    protected internal override async ValueTask PerformMeleeAttackAsync(IEntity target)
    {
        if (!CanAttack) return;
        attackTicks = 10;
        SendEntityEvent(4);
        PlayMobSound("attack");
        var health = target.Health;
        await base.PerformMeleeAttackAsync(target);
        if (target.Health < health && target is Entity entity) PushAway(entity, GetAttributeValue("minecraft:generic.attack_knockback"));
    }
    private void PushAway(Entity target, double strength)
    {
        var delta = target.Position - Position;
        var distance = Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
        strength *= 1 - Math.Clamp(target.GetAttributeValue("minecraft:generic.knockback_resistance"), 0, 1);
        if (distance < 0.0001 || strength <= 0) return;
        target.Motion += new VectorD(delta.X / distance * strength, 0.2, delta.Z / distance * strength);
    }
    protected override ValueTask OnDeathAsync(IEntity source) { DropItem(Material.Saddle); return default; }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.Boolean);
        writer.WriteBoolean(false);
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        writer.WriteInt("AttackTick", attackTicks);
        writer.WriteInt("StunTick", stunTicks);
        writer.WriteInt("RoarTick", roarTicks);
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        attackTicks = Math.Clamp(tag.TryGetTagValue<int>("AttackTick", out var attack) ? attack : 0, 0, 10);
        stunTicks = Math.Clamp(tag.TryGetTagValue<int>("StunTick", out var stun) ? stun : 0, 0, 40);
        roarTicks = Math.Clamp(tag.TryGetTagValue<int>("RoarTick", out var roar) ? roar : 0, 0, 20);
    }
}

internal sealed class RavagerMeleeGoal(Ravager ravager) : MeleeAttackGoal(ravager)
{
    public override bool CanUse() => ravager.CanAttack && base.CanUse();
    public override bool CanContinue() => ravager.CanAttack && base.CanContinue();
}
