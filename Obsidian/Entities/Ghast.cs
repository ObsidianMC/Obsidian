using Obsidian.Entities.AI;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:ghast")]
public sealed partial class Ghast : Mob
{
    private VectorD destination;
    private int flightTicks;
    private int attackTicks;
    internal bool Charging { get; private set; }
    public Ghast() { Type = EntityType.Ghast; NoGravity = true; }
    protected override bool UsesAi => true;
    internal override bool Hostile => true;
    protected override bool TakesFallDamage => false;
    protected override string? SoundName => "ghast";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override int GetExperienceReward() => 5;
    protected override ValueTask TickMobAsync()
    {
        if (MobBitMask.HasFlag(MobBitmask.NoAi))
            return default;
        if (AttackTarget == null || !IsValidTarget(AttackTarget) || (AttackTarget.Position - Position).MagnitudeSquared() > 10000)
            AttackTarget = Level.GetPlayersInRange(Position, 100).Where(player => IsValidTarget(player) && CanSee(player))
                .MinBy(player => (player.Position - Position).MagnitudeSquared());
        if (--flightTicks <= 0)
        {
            flightTicks = Random.Next(2, 7);
            if ((destination - Position).MagnitudeSquared() < 1 || (destination - Position).MagnitudeSquared() > 3600)
                destination = Position + new VectorD((Random.NextSingle() * 2 - 1) * 16,
                    (Random.NextSingle() * 2 - 1) * 16, (Random.NextSingle() * 2 - 1) * 16);
            var delta = destination - Position;
            if (delta.Magnitude > 0.001f)
            {
                var direction = delta / delta.Magnitude;
                var bounds = Dimension.CreateBBFromPosition(Position);
                var clear = true;
                for (var distance = 1f; distance < delta.Magnitude; distance++)
                    if (!Terrain.IsFree(bounds.OffsetBy(direction * distance))) { clear = false; break; }
                if (clear)
                    Motion += direction * 0.1f;
                else
                    destination = Position;
            }
        }
        if (AttackTarget is { } target && (target.Position - Position).MagnitudeSquared() < 4096 && CanSee(target))
        {
            LookControl.LookAt(target);
            Yaw = (float)(Math.Atan2(-(target.Position.X - Position.X), target.Position.Z - Position.Z) * 180 / Math.PI);
            if (++attackTicks == 10)
                PlayMobSound("warn");
            if (attackTicks == 20)
            {
                var look = GetLookDirection();
                var origin = Position + new VectorD(0, Dimension.Height * 0.5f + 0.5f, 0) + look * 4;
                Level.SpawnEntity(new MobProjectile(this, EntityType.Fireball, origin,
                    target.Position + new VectorD(0, target.Dimension.Height * 0.5f, 0) - origin));
                PlayMobSound("shoot");
                attackTicks = -40;
            }
        }
        else
        {
            if (attackTicks > 0) attackTicks--;
            if (AttackTarget == null && Motion.MagnitudeSquared() > 0.001f)
                Yaw = (float)(Math.Atan2(-Motion.X, Motion.Z) * 180 / Math.PI);
        }
        var charging = attackTicks > 10;
        if (Charging != charging) { Charging = charging; SynchronizeMetadata(); }
        return default;
    }
    protected override VectorD Travel() => EntityMovement.Move(this, Terrain, VectorD.Zero, 0, 0.91f);
    protected override ValueTask OnDeathAsync(IEntity source)
    {
        DropItem(Material.Gunpowder, Random.Next(3));
        DropItem(Material.GhastTear, Random.Next(2));
        return default;
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.Boolean);
        writer.WriteBoolean(Charging);
    }
}
