using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:warden")]
public sealed partial class Warden : PathfinderMob
{
    private const int AngryThreshold = 80;
    private readonly Dictionary<Guid, int> anger = [];
    private int projectileMemoryTicks;
    private int idleTicks;
    private int poseTicks;
    private int vibrationCooldown;
    private int sniffCooldown = 100;
    private int meleeCooldown;
    private int sonicCooldown = 200;
    private int sonicTicks;
    private int clientAnger;
    private int touchCooldown;

    internal void BeginEmerging()
    {
        Pose = Pose.Emerging;
        poseTicks = 134;
    }

    internal bool AcceptsVibration(MobVibration vibration) => Alive && !MobBitMask.HasFlag(MobBitmask.NoAi) &&
        vibrationCooldown == 0 && Pose is not (Pose.Digging or Pose.Emerging) &&
        vibration.Source != this && (vibration.Source is not ILiving || CanSense(vibration.Source));

    internal void ReceiveVibration(MobVibration vibration)
    {
        if (!AcceptsVibration(vibration)) return;
        vibrationCooldown = 40;
        idleTicks = 0;
        SendEntityEvent(61);
        PlayMobSound("tendril_clicks");
        var destination = vibration.Position;
        if (vibration.Owner is { } owner)
        {
            if (IsInRange(owner, 30))
            {
                IncreaseAnger(owner, projectileMemoryTicks > 0 ? 35 : 10);
                if (projectileMemoryTicks > 0 && CanSense(owner)) destination = owner.Position;
            }
            projectileMemoryTicks = 100;
        }
        else if (vibration.Source is { } source) IncreaseAnger(source, 35);
        if (AttackTarget == null && poseTicks == 0) Navigator?.NavigateTo(destination);
    }

    public Warden()
    {
        Type = EntityType.Warden;
        IsFireImmune = true;
    }

    protected override bool UsesAi => true;
    internal override bool Hostile => true;
    protected override bool CanDespawn => false;
    protected override bool TakesFallDamage => false;
    protected override string? SoundName => "warden";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override int GetExperienceReward() => 5;
    internal override float MovementSpeed => poseTicks > 0 || sonicTicks > 0 ? 0 : base.MovementSpeed;
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(8, new WardenStrollGoal(this));
    }

    private bool CanSense(IEntity entity) => entity is ILiving && entity.Type != EntityType.Warden && IsValidTarget(entity);

    private void IncreaseAnger(IEntity entity, int amount)
    {
        if (!CanSense(entity)) return;
        anger[entity.Uuid] = Math.Min(150, anger.GetValueOrDefault(entity.Uuid) + amount);
        idleTicks = 0;
    }

    protected override bool CanTakeDamage(IEntity source) => Pose is not (Pose.Digging or Pose.Emerging);

    protected override ValueTask OnHurtAsync(IEntity source)
    {
        IncreaseAnger(source, 100);
        if (Pose is Pose.Digging or Pose.Sniffing)
        {
            poseTicks = 0;
            SetPose(Pose.Standing);
        }
        return default;
    }

    private void SetPose(Pose pose)
    {
        if (Pose == pose) return;
        Pose = pose;
        SynchronizeMetadata();
    }

    protected override async ValueTask TickMobAsync()
    {
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return;
        if (meleeCooldown > 0) meleeCooldown--;
        if (sonicCooldown > 0) sonicCooldown--;
        if (vibrationCooldown > 0) vibrationCooldown--;
        if (projectileMemoryTicks > 0) projectileMemoryTicks--;
        if (touchCooldown > 0) touchCooldown--;
        if (sniffCooldown > 0) sniffCooldown--;
        var nearby = GetEntitiesNear(32).Where(CanSense).ToArray();
        if (touchCooldown == 0 && nearby.FirstOrDefault(entity => IsInRange(entity, 1.5f)) is { } touching)
        {
            IncreaseAnger(touching, 35);
            touchCooldown = 20;
        }
        if (AiTick % 20 == 0)
        {
            foreach (var id in anger.Keys.ToArray())
                if (--anger[id] <= 0) anger.Remove(id);
        }
        var mostAngry = nearby.Where(entity => anger.GetValueOrDefault(entity.Uuid) >= AngryThreshold)
            .OrderByDescending(entity => anger[entity.Uuid]).ThenBy(entity => entity is IPlayer ? 0 : 1)
            .ThenBy(entity => (entity.Position - Position).MagnitudeSquared()).FirstOrDefault();
        if (AttackTarget != null && (!CanSense(AttackTarget) || !IsInRange(AttackTarget, 32))) AttackTarget = null;
        if (AttackTarget == null && mostAngry != null && poseTicks == 0)
        {
            AttackTarget = mostAngry;
            poseTicks = 84;
            SetPose(Pose.Roaring);
            PlayMobSound("roar");
            sonicCooldown = 200;
        }
        var nextAnger = nearby.Select(entity => anger.GetValueOrDefault(entity.Uuid)).DefaultIfEmpty(0).Max();
        if (nextAnger != clientAnger)
        {
            clientAnger = nextAnger;
            SynchronizeMetadata();
        }
        if (AiTick % 120 == 0)
            foreach (var player in Level.GetPlayersInRange(Position, 20).Where(player => player.GameMode != GameMode.Spectator))
                player.AddPotionEffect((int)PotionEffect.Darkness - 1, 260);

        if (poseTicks > 0)
        {
            (Navigator as Navigator)?.Stop();
            if (--poseTicks == 0)
            {
                if (Pose == Pose.Digging) { await RemoveAsync(); return; }
                if (Pose == Pose.Sniffing)
                {
                    var smelled = nearby.Where(entity => Math.Abs(entity.Position.Y - Position.Y) <= 20 &&
                        Math.Pow(entity.Position.X - Position.X, 2) + Math.Pow(entity.Position.Z - Position.Z, 2) <= 36)
                        .MinBy(entity => (entity.Position - Position).MagnitudeSquared());
                    if (smelled != null) { IncreaseAnger(smelled, 35); Navigator?.NavigateTo(smelled.Position); }
                }
                SetPose(Pose.Standing);
            }
            return;
        }
        if (AttackTarget is { } target)
        {
            idleTicks = 0;
            LookControl.LookAt(target.Position + new VectorD(0, target.Dimension.Height * 0.85f, 0), 30, 30);
            if (sonicTicks > 0)
            {
                (Navigator as Navigator)?.Stop();
                if (--sonicTicks == 26)
                {
                    PlayMobSound("sonic_boom");
                    var beam = target.Position + new VectorD(0, target.Dimension.Height * 0.85f, 0) - EyePosition;
                    var beamLength = beam.Magnitude;
                    if (beamLength > 0)
                        for (var step = 1; step < beamLength + 7; step++)
                            PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new LevelParticlesPacket
                            { Position = EyePosition + beam / beamLength * step, ParticleCount = 1, Data = new SonicParticle() });
                    if (InSonicRange(target))
                    {
                        var damage = target is IPlayer ? Level.LevelData.Difficulty switch
                        { Difficulty.Easy => 6, Difficulty.Hard => 15, Difficulty.Peaceful => 0, _ => 10 } : 10;
                        if (target is Mob mob) await mob.DamageMagicAsync(this, damage, ignoreHurtCooldown: true);
                        else await target.DamageAsync(this, damage);
                        if (target is Entity entity)
                        {
                            var delta = target.Position - Position;
                            var length = Math.Sqrt(delta.MagnitudeSquared());
                            var resistance = 1 - Math.Clamp(entity.GetAttributeValue("minecraft:generic.knockback_resistance"), 0, 1);
                            if (length > 0) entity.Motion += new VectorD(delta.X / length * 2.5 * resistance,
                                delta.Y / length * 0.5 * resistance, delta.Z / length * 2.5 * resistance);
                        }
                    }
                }
                return;
            }
            if (IsWithinMeleeRange(target) && meleeCooldown == 0)
            {
                meleeCooldown = 18;
                sonicCooldown = 40;
                SendEntityEvent(4);
                PlayMobSound("attack_impact");
                var health = target.Health;
                await base.PerformMeleeAttackAsync(target);
                if (target.Health < health && target is Entity hit)
                {
                    var delta = target.Position - Position;
                    var horizontal = Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
                    var strength = GetAttributeValue("minecraft:generic.attack_knockback") *
                        (1 - Math.Clamp(hit.GetAttributeValue("minecraft:generic.knockback_resistance"), 0, 1));
                    if (horizontal > 0 && strength > 0)
                        hit.Motion += new VectorD(delta.X / horizontal * strength, 0.1, delta.Z / horizontal * strength);
                }
            }
            else if (sonicCooldown == 0 && InSonicRange(target))
            {
                sonicTicks = 60;
                sonicCooldown = 100;
                SendEntityEvent(62);
                PlayMobSound("sonic_charge");
            }
            else
            {
                if (Navigator is Navigator navigation) navigation.SpeedModifier = 1.2f;
                Navigator?.NavigateTo(target);
            }
            return;
        }
        sonicTicks = 0;
        if (++idleTicks >= 1200 && !PersistenceRequired && CustomName == null && !InWater && !InLava)
        {
            poseTicks = 100;
            SetPose(Pose.Digging);
            PlayMobSound("dig");
        }
        else if (sniffCooldown == 0)
        {
            sniffCooldown = 100 + Random.Next(100);
            poseTicks = 83;
            SetPose(Pose.Sniffing);
            PlayMobSound("sniff");
        }
    }

    private sealed class SonicParticle : ParticleData
    {
        public override ParticleType ParticleType => ParticleType.SonicBoom;
    }

    private bool InSonicRange(IEntity target)
    {
        var delta = target.Position - Position;
        return delta.X * delta.X + delta.Z * delta.Z <= 225 && Math.Abs(delta.Y) <= 20;
    }

    protected override ValueTask OnDeathAsync(IEntity source) { DropItem(Material.SculkCatalyst); return default; }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.VarInt);
        writer.WriteVarInt(clientAnger);
    }

    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        writer.WriteInt("ObsidianWardenIdle", idleTicks);
        writer.WriteInt("ObsidianWardenSniffCooldown", sniffCooldown);
        writer.WriteInt("ObsidianWardenVibrationCooldown", vibrationCooldown);
        writer.WriteInt("ObsidianWardenMeleeCooldown", meleeCooldown);
        writer.WriteInt("ObsidianWardenTouchCooldown", touchCooldown);
        writer.WriteInt("ObsidianWardenProjectileMemory", projectileMemoryTicks);
        writer.WriteInt("ObsidianWardenPoseTicks", poseTicks);
        writer.WriteInt("ObsidianWardenPose", (int)Pose);
        writer.WriteInt("ObsidianWardenSonicCooldown", sonicCooldown);
        writer.WriteInt("ObsidianWardenSonicTicks", sonicTicks);
        writer.WriteListStart("ObsidianWardenAnger", NbtTagType.Compound, anger.Count);
        foreach (var (id, value) in anger)
        {
            writer.WriteCompoundStart();
            writer.WriteString("uuid", id.ToString());
            writer.WriteInt("anger", value);
            writer.EndCompound();
        }
        writer.EndList();
    }

    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        idleTicks = ReadTimer(tag, "ObsidianWardenIdle", 1200);
        sniffCooldown = ReadTimer(tag, "ObsidianWardenSniffCooldown", 200);
        vibrationCooldown = ReadTimer(tag, "ObsidianWardenVibrationCooldown", 40);
        meleeCooldown = ReadTimer(tag, "ObsidianWardenMeleeCooldown", 18);
        touchCooldown = ReadTimer(tag, "ObsidianWardenTouchCooldown", 20);
        projectileMemoryTicks = ReadTimer(tag, "ObsidianWardenProjectileMemory", 100);
        poseTicks = ReadTimer(tag, "ObsidianWardenPoseTicks", 134);
        sonicCooldown = ReadTimer(tag, "ObsidianWardenSonicCooldown", 200);
        sonicTicks = ReadTimer(tag, "ObsidianWardenSonicTicks", 60);
        if (tag.TryGetTagValue<int>("ObsidianWardenPose", out var pose) &&
            (Pose)pose is Pose.Standing or Pose.Roaring or Pose.Sniffing or Pose.Digging or Pose.Emerging)
            Pose = (Pose)pose;
        anger.Clear();
        if (tag.TryGetTag<NbtList>("ObsidianWardenAnger", out var saved))
            foreach (var entry in saved.OfType<NbtCompound>())
                if (entry.TryGetTagValue<string>("uuid", out var text) && Guid.TryParse(text, out var id) &&
                    entry.TryGetTagValue<int>("anger", out var value) && value > 0)
                    anger[id] = Math.Min(150, value);
    }

    private static int ReadTimer(NbtCompound tag, string name, int maximum) =>
        tag.TryGetTagValue<int>(name, out var value) ? Math.Clamp(value, 0, maximum) : 0;
}

internal sealed class WardenStrollGoal(Warden warden) : NavigationGoal(warden, 0.5f)
{
    private VectorD destination;
    public override bool CanUse()
    {
        if (warden.Pose != Pose.Standing || warden.AttackTarget != null || warden.Random.Next(60) != 0 ||
            RandomPosition.Find(warden, 10) is not VectorD point) return false;
        destination = point;
        return true;
    }
    public override void Start() => MoveTo(destination);
    public override bool CanContinue() => warden.Pose == Pose.Standing && warden.AttackTarget == null && Navigation.IsNavigating;
}
