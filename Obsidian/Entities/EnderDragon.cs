using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:ender_dragon")]
public sealed partial class EnderDragon : BossMob
{
    private enum DragonPhase { Holding, Strafing, LandingApproach, Landing, Takeoff, Flaming, Scanning, Attacking, Charging, Dying, Hovering }
    private DragonPhase phase = DragonPhase.Hovering;
    private int phaseTicks;
    private int flameCount;
    private int flightPoint;
    private int fireballCharge;
    private int chargeRecoveryTicks;
    private long lastDamageTick = -100;
    private float lastDamage;
    private float sittingDamage;
    private VectorD destination;
    private VectorD arena;
    private bool arenaKnown;
    private bool fightControlled;
    private VectorD? podium;
    private EndCrystal? crystal;
    private DragonBreathCloud? breath;
    private EnderDragonPart[] parts = [];
    private readonly Dictionary<int, long> contactTicks = [];
    public bool PreviouslyKilled { get; internal set; } = true;
    internal bool IsPerched => phase is DragonPhase.Flaming or DragonPhase.Scanning or DragonPhase.Attacking or DragonPhase.Hovering;
    internal IReadOnlyList<EnderDragonPart> Parts { get { UpdateParts(); return parts; } }

    public EnderDragon()
    {
        Type = EntityType.EnderDragon;
        NoGravity = true;
        IsFireImmune = true;
        PersistenceRequired = true;
    }
    protected override bool UsesAi => true;
    protected override Obsidian.API.Boss.BossBarColor BarColor => Obsidian.API.Boss.BossBarColor.Pink;
    protected override Obsidian.API.Boss.BossBarFlags BarFlags => Obsidian.API.Boss.BossBarFlags.DarkenSky | Obsidian.API.Boss.BossBarFlags.DragonBar;
    protected override bool ShowBossBar => fightControlled;
    protected override IEnumerable<IPlayer> BarPlayers => Level.GetPlayersInRange(new VectorD(0, 128, 0), 192)
        .Where(player => player.GameMode != GameMode.Spectator);
    protected override bool CanTakeDamage(IEntity source) => source is IPlayer or EndCrystal;
    internal override bool Hostile => false;
    protected override bool CanDespawn => false;
    protected override bool TakesFallDamage => false;
    protected override ValueTask TickAirSupplyAsync() => default;
    protected override string? SoundName => "ender_dragon";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;

    internal void BeginFight(VectorD fountain)
    {
        AttachFight(fountain, PreviouslyKilled);
        SetPhase(DragonPhase.Holding);
    }

    internal void AttachFight(VectorD fountain, bool previouslyKilled)
    {
        arena = fountain;
        arenaKnown = true;
        podium = null;
        fightControlled = true;
        PreviouslyKilled = previouslyKilled;
    }

    internal void EnsureMultipartIds()
    {
        if (parts.Length != 0) return;
        EntityId = Server.ReserveEntityIds(9);
        (float Width, float Height)[] sizes = [(1, 1), (3, 3), (5, 3), (2, 2), (2, 2), (2, 2), (4, 2), (4, 2)];
        parts = sizes.Select((size, index) => new EnderDragonPart(this, index,
            new EntityDimension { Width = size.Width, Height = size.Height })).ToArray();
        UpdateParts();
    }

    private void UpdateParts()
    {
        var angle = Yaw.Degrees * Math.PI / 180;
        var forward = new VectorD(Math.Sin(angle), 0, -Math.Cos(angle));
        var side = new VectorD(Math.Cos(angle), 0, Math.Sin(angle));
        var pitch = (FlightSample(5).Height - FlightSample(10).Height) * 10 * Math.PI / 180;
        var pitchCos = Math.Cos(pitch);
        var pitchSin = Math.Sin(pitch);
        var headAngle = angle - yawVelocity * 0.01;
        var headForward = new VectorD(Math.Sin(headAngle), 0, -Math.Cos(headAngle));
        var headHeight = IsPerched ? -1 : FlightSample(5).Height - FlightSample(0).Height;
        for (var index = 0; index < parts.Length; index++)
        {
            var offset = index switch
            {
                0 => headForward * (6.5 * pitchCos) + new VectorD(0, headHeight + pitchSin * 6.5, 0),
                1 => headForward * (5.5 * pitchCos) + new VectorD(0, headHeight + pitchSin * 5.5, 0),
                2 => forward * 0.5f,
                3 or 4 or 5 => TailOffset(index - 3, forward, pitchCos, pitchSin),
                6 => side * 4.5f + new VectorD(0, 2, 0),
                _ => side * -4.5f + new VectorD(0, 2, 0)
            };
            parts[index].SetPosition(Position + offset);
        }
    }

    protected override VectorD Travel()
    {
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return Position;
        return Position + flightDisplacement;
    }

    protected override async ValueTask TickMobAsync()
    {
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) { Motion = VectorD.Zero; return; }
        if (!arenaKnown)
        {
            // Summoned dragons use their summon position; End dragons use the exit fountain.
            arena = new VectorD(Position.X, Position.Y, Position.Z);
            if (Level.DimensionName == "minecraft:the_end")
            {
                var y = 64;
                for (var scan = 255; scan >= 0; scan--)
                    if (Terrain.GetBlock(new Vector(0, scan, 0)) is { IsAir: false }) { y = scan + 1; break; }
                arena = new VectorD(0, y, 0);
            }
            arenaKnown = true;
        }
        if (AiTick % 20 == 0) podium = null;
        RecordFlight();
        UpdateParts();
        phaseTicks++;
        if (phase != DragonPhase.Dying && AiTick % 10 == 0 && crystal is { Health: > 0 })
        {
            Health = Math.Min(GetAttributeValue("minecraft:generic.max_health"), Health + 1);
            SynchronizeMetadata();
        }
        if (AiTick % 20 == 0)
            crystal = Level.GetEntitiesInRange(Position, 32).OfType<EndCrystal>().Where(item => item.Health > 0)
                .MinBy(item => (item.Position - Position).MagnitudeSquared());
        switch (phase)
        {
            case DragonPhase.Holding:
                TickHolding();
                break;
            case DragonPhase.Hovering:
                Motion = VectorD.Zero;
                break;
            case DragonPhase.Strafing:
                if (AttackTarget is { } target && IsValidTarget(target))
                {
                    TickStrafingPath(target);
                    var origin = parts[0].Position + DragonForward + new VectorD(0, 0.5, 0);
                    var direction = target.Position + new VectorD(0, target.Dimension.Height / 2, 0) - origin;
                    if ((target.Position - Position).MagnitudeSquared() < 4096 && CanSee(target))
                    {
                        fireballCharge++;
                        var look = DragonForward;
                        var horizontalDirection = new VectorD(target.Position.X - Position.X, 0, target.Position.Z - Position.Z);
                        if (fireballCharge >= 5 && horizontalDirection.Magnitude > 0.001 && VectorD.Dot(horizontalDirection / horizontalDirection.Magnitude, look) > Math.Cos(10 * Math.PI / 180))
                        {
                            Level.SpawnEntity(new DragonFireball(this, origin, direction));
                            PlayMobSound("shoot");
                            SetPhase(DragonPhase.Holding);
                        }
                    }
                    else if (fireballCharge > 0) fireballCharge--;
                }
                else SetPhase(DragonPhase.Holding);
                break;
            case DragonPhase.LandingApproach:
                TickLandingApproach();
                break;
            case DragonPhase.Landing:
                destination = Podium;
                if ((destination - Position).MagnitudeSquared() < 1) { SetPhase(DragonPhase.Scanning); flameCount = 0; sittingDamage = 0; }
                break;
            case DragonPhase.Scanning:
                Motion = VectorD.Zero;
                AttackTarget = Level.GetPlayersInRange(Position, 20).Where(target => IsValidTarget(target) && Math.Abs(target.Position.Y - Position.Y) < 10)
                    .MinBy(target => (target.Position - Position).MagnitudeSquared());
                if (AttackTarget != null) Face(AttackTarget.Position);
                if (phaseTicks > 25 && AttackTarget != null)
                    SetPhase(DragonPhase.Attacking);
                else if (phaseTicks >= 100 && AttackTarget == null)
                {
                    var chargeTarget = Level.GetPlayersInRange(Position, 150).Where(IsValidTarget)
                        .MinBy(target => (target.Position - Position).MagnitudeSquared());
                    SetPhase(DragonPhase.Takeoff);
                    if (chargeTarget != null)
                    {
                        SetPhase(DragonPhase.Charging);
                        destination = chargeTarget.Position;
                    }
                }
                break;
            case DragonPhase.Attacking:
                Motion = VectorD.Zero;
                if (phaseTicks == 1) PlayMobSound("growl");
                if (phaseTicks >= 40) SetPhase(DragonPhase.Flaming);
                break;
            case DragonPhase.Flaming:
                Motion = VectorD.Zero;
                if (phaseTicks == 10)
                {
                    var origin = parts[0].Position + DragonForward * 2.5f;
                    var y = (int)Math.Floor(origin.Y);
                    while (y > arena.Y - 16 && Terrain.GetBlock(new Vector((int)Math.Floor(origin.X), y - 1, (int)Math.Floor(origin.Z)))?.IsAir == true) y--;
                    origin.Y = y;
                    breath = new DragonBreathCloud { Level = Level, EntityId = Server.GetNextEntityId(), Position = origin, OwnerUuid = Uuid, Radius = 5, Duration = 200 };
                    Level.SpawnEntity(breath);
                    PlayMobSound("shoot");
                }
                if (phaseTicks >= 200)
                {
                    if (breath != null) { await breath.RemoveAsync(); breath = null; }
                    SetPhase(++flameCount >= 4 ? DragonPhase.Takeoff : DragonPhase.Scanning);
                }
                break;
            case DragonPhase.Takeoff:
                TickTakeoff();
                break;
            case DragonPhase.Charging:
                if (chargeRecoveryTicks > 0 && chargeRecoveryTicks++ >= 10) SetPhase(DragonPhase.Holding);
                else
                {
                    var chargeDistance = (destination - Position).MagnitudeSquared();
                    if (chargeDistance < 100 || chargeDistance > 22500 || MovementFlags.HasFlag(MovementFlags.HorizontalCollision))
                        chargeRecoveryTicks++;
                }
                break;
            case DragonPhase.Dying:
                destination = Podium;
                var deathDistance = (destination - Position).MagnitudeSquared();
                if (deathDistance < 100 || deathDistance > 22500 || MovementFlags.HasFlag(MovementFlags.HorizontalCollision))
                {
                    await KillAsync(this);
                    return;
                }
                break;
        }
        if (!IsPerched) SteerFlight();
        else flightDisplacement = VectorD.Zero;
        await ContactAttackAsync();
        await DestroyBlocksAsync();
    }

    private async ValueTask DestroyBlocksAsync()
    {
        if (Level is not AbstractLevel level || level.Generator is Obsidian.WorldData.Generators.MobTestGenerator)
            return;
        var griefing = Level.LevelData.GetBooleanRule("mob_griefing");
        var blocked = false;
        foreach (var part in parts.Take(3))
        {
            var bounds = part.BoundingBox;
            for (var x = (int)Math.Floor(bounds.Min.X); x <= (int)Math.Floor(bounds.Max.X); x++)
            for (var y = (int)Math.Floor(bounds.Min.Y); y <= (int)Math.Floor(bounds.Max.Y); y++)
            for (var z = (int)Math.Floor(bounds.Min.Z); z <= (int)Math.Floor(bounds.Max.Z); z++)
            {
                var position = new Vector(x, y, z);
                if (Terrain.GetBlock(position) is not { IsAir: false } block ||
                    TagsRegistry.Block.DragonTransparent.Entries.Contains(block.RegistryId)) continue;
                if (!griefing || TagsRegistry.Block.DragonImmune.Entries.Contains(block.RegistryId)) blocked = true;
                else await level.SetBlockAsync(position, BlocksRegistry.Air, true);
            }
        }
        if (blocked) Motion *= 0.8f;
    }

    // Vanilla's EndPodiumFeature location under the MOTION_BLOCKING_NO_LEAVES heightmap: the top of the
    // bedrock pillar (or the egg on it), where the dragon perches and dies.
    private VectorD Podium
    {
        get
        {
            if (podium is { } cached) return cached;
            var x = (int)Math.Floor(arena.X);
            var z = (int)Math.Floor(arena.Z);
            var top = arena.Y;
            for (var y = (int)Math.Floor(arena.Y) + 10; y >= (int)Math.Floor(arena.Y); y--)
                if (Terrain.GetBlock(new Vector(x, y, z)) is { IsAir: false }) { top = y + 1; break; }
            return (podium = new VectorD(x + 0.5, top, z + 0.5)).Value;
        }
    }

    private void Face(VectorD target)
    {
        var desired = 180 - Math.Atan2(target.X - Position.X, target.Z - Position.Z) * 180 / Math.PI;
        var difference = WrapDegrees(desired - Yaw.Degrees);
        Yaw = (float)(Yaw.Degrees + Math.Clamp(difference, -10, 10));
    }
    private void SetPhase(DragonPhase next)
    {
        phase = next;
        phaseTicks = 0;
        flightPath.Clear();
        hasFlightDestination = false;
        fireballCharge = 0;
        chargeRecoveryTicks = 0;
        SynchronizeMetadata();
    }
    private async ValueTask ContactAttackAsync()
    {
        if (parts.Length == 0) return;
        foreach (var target in Level.GetEntitiesInRange(Position, 16).OfType<Living>().ToArray())
        {
            if (ReferenceEquals(target, this) || !target.Alive || target is IPlayer { GameMode: GameMode.Creative or GameMode.Spectator }) continue;
            var bounds = target.Dimension.CreateBBFromPosition(target.Position);
            for (var index = 6; index < 8; index++)
            {
                var wing = parts[index].BoundingBox;
                var area = new BoundingBox(wing.Min - new VectorD(4, 4, 4), wing.Max + new VectorD(4, 0, 4));
                if (!area.Intersects(bounds)) continue;
                var offset = target.Position - parts[2].Position;
                var divisor = Math.Max(0.1, offset.X * offset.X + offset.Z * offset.Z);
                target.Motion += new VectorD(offset.X / divisor * 4, 0.2f, offset.Z / divisor * 4);
                if (!IsPerched && (!contactTicks.TryGetValue(target.EntityId, out var tick) || AiTick - tick > 2))
                {
                    var health = target.Health;
                    await target.DamageAsync(this, 5);
                    if (target.Health < health) contactTicks[target.EntityId] = AiTick;
                }
            }
            for (var index = 0; index < 2; index++)
            {
                var head = parts[index].BoundingBox;
                if (new BoundingBox(head.Min - new VectorD(1), head.Max + new VectorD(1)).Intersects(bounds))
                    await target.DamageAsync(this, 10);
            }
        }
        if (AiTick % 100 == 0)
            foreach (var id in contactTicks.Where(entry => AiTick - entry.Value > 100).Select(entry => entry.Key).ToArray()) contactTicks.Remove(id);
    }

    public override ValueTask DamageAsync(IEntity source, float amount = 1) => DamagePartAsync(source, amount, 2);
    internal async ValueTask DamagePartAsync(IEntity source, float amount, int part, bool projectile = false)
    {
        if (IsRemoved || phase == DragonPhase.Dying || source.Level != Level || !float.IsFinite(amount) || amount <= 0 ||
            source is not IPlayer && source is not EndCrystal && IncomingDamageKind != CombatDamageKind.Explosion || IsPerched && projectile) return;
        if (part != 0) amount = amount / 4 + Math.Min(amount, 1);
        if (AiTick - lastDamageTick < 10)
        {
            if (amount <= lastDamage) return;
            var incoming = amount;
            amount -= lastDamage;
            lastDamage = incoming;
        }
        else { lastDamageTick = AiTick; lastDamage = amount; }
        Health -= amount;
        PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new HurtAnimationPacket { EntityId = EntityId, Yaw = 0 });
        PlayMobSound("hurt");
        if (Health <= 0)
        {
            if (IsPerched) await KillAsync(source);
            else
            {
                Health = 1;
                SetPhase(DragonPhase.Dying);
            }
        }
        if (IsPerched)
        {
            sittingDamage += amount;
            if (sittingDamage > GetAttributeValue("minecraft:generic.max_health") * 0.25f)
            {
                sittingDamage = 0;
                if (breath != null) { await breath.RemoveAsync(); breath = null; }
                SetPhase(DragonPhase.Takeoff);
            }
        }
        SynchronizeMetadata();
    }
    protected override int DeathDuration => 200;
    protected override async ValueTask OnDeathAsync(IEntity source)
    {
        if (breath != null) { await breath.RemoveAsync(); breath = null; }
        SetPhase(DragonPhase.Dying);
    }
    protected override async ValueTask TickDeathAsync()
    {
        phaseTicks++;
        if (phaseTicks == 1)
            PacketBroadcaster.QueuePacketToLevel(Level, new LevelEventPacket(1028, (Vector)Position.Floor(), 0));
        if (phaseTicks >= 180 && phaseTicks <= 200)
            PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new LevelParticlesPacket
            {
                Position = Position + new VectorD((Random.NextDouble() - 0.5) * 8, 2 + (Random.NextDouble() - 0.5) * 4, (Random.NextDouble() - 0.5) * 8),
                ParticleCount = 1, Data = new DragonDeathParticle()
            });
        Motion = new VectorD(0, 0.1f, 0);
        Yaw = Yaw.Degrees + 20;
        var next = Position + Motion;
        if (Level is AbstractLevel level && level.TryMoveEntity(this, Position, next))
        {
            Position = next;
            BoundingBox = Dimension.CreateBBFromPosition(Position);
            PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new TeleportEntityPacket
            { EntityId = EntityId, Position = Position, Delta = Motion, Yaw = Yaw, Pitch = Pitch, OnGround = false });
        }
        if (phaseTicks > 150 && phaseTicks % 5 == 0 && Level.LevelData.GetBooleanRule("mob_drops"))
            Level.SpawnExperienceOrbs(Position, (short)(PreviouslyKilled ? 40 : 960));
        if (phaseTicks >= 200)
        {
            if (Level.LevelData.GetBooleanRule("mob_drops")) Level.SpawnExperienceOrbs(Position, (short)(PreviouslyKilled ? 100 : 2400));
            if (Level is AbstractLevel fightLevel) await fightLevel.EndFightDragonKilledAsync(this);
            await RemoveAsync();
        }
    }
    private sealed class DragonDeathParticle : ParticleData
    {
        public override ParticleType ParticleType => ParticleType.ExplosionEmitter;
    }
    internal async ValueTask OnCrystalDestroyedAsync(EndCrystal destroyed, IEntity source)
    {
        if (ReferenceEquals(crystal, destroyed))
        {
            crystal = null;
            await DamagePartAsync(destroyed, 10, 0);
        }
        if (phase == DragonPhase.Holding && source is IPlayer && IsValidTarget(source))
        {
            AttackTarget = source;
            SetPhase(DragonPhase.Strafing);
        }
    }
    public override async ValueTask RemoveAsync()
    {
        if (breath != null) { await breath.RemoveAsync(); breath = null; }
        await base.RemoveAsync();
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.VarInt);
        writer.WriteVarInt((int)phase);
    }
    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);
        WriteDragonNbt(tag);
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        var tag = new NbtCompound();
        WriteDragonNbt(tag);
        foreach (var (_, child) in tag) writer.WriteTag(child);
    }
    private void WriteDragonNbt(NbtCompound tag)
    {
        tag.Set(new NbtTag<int>("DragonPhase", (int)phase));
        tag.Set(new NbtTag<int>("DragonDeathTime", !Alive && phase == DragonPhase.Dying ? phaseTicks : 0));
        tag.Set(new NbtTag<bool>("ObsidianPreviouslyKilled", PreviouslyKilled));
        tag.Set(new NbtTag<int>("ObsidianPhaseTicks", phaseTicks));
        tag.Set(new NbtTag<int>("ObsidianFlameCount", flameCount));
        tag.Set(new NbtTag<int>("ObsidianFlightPoint", flightPoint));
        tag.Set(new NbtTag<float>("ObsidianSittingDamage", sittingDamage));
        tag.Set(EntityNbt.DoubleList("ObsidianDestination", destination));
        if (arenaKnown) tag.Set(EntityNbt.DoubleList("ObsidianDragonArena", arena));
    }
    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);
        ReadDragonNbt(tag);
    }
    protected override void ReadAdditionalSave(NbtCompound tag) => ReadDragonNbt(tag);
    private void ReadDragonNbt(NbtCompound tag)
    {
        phase = (DragonPhase)Math.Clamp(tag.TryGetTagValue<int>("DragonPhase", out var saved) ? saved : 10, 0, 10);
        phaseTicks = phase == DragonPhase.Dying && tag.TryGetTagValue<int>("DragonDeathTime", out var ticks) ? Math.Clamp(ticks, 0, 199) : 0;
        PreviouslyKilled = !tag.TryGetBool("ObsidianPreviouslyKilled", out var killed) || killed;
        arenaKnown = EntityNbt.TryReadVector(tag, "ObsidianDragonArena", out arena);
        if (tag.TryGetTagValue<int>("ObsidianPhaseTicks", out var phaseTime)) phaseTicks = Math.Clamp(phaseTime, 0, 72000);
        if (tag.TryGetTagValue<int>("ObsidianFlameCount", out var flames)) flameCount = Math.Clamp(flames, 0, 4);
        if (tag.TryGetTagValue<int>("ObsidianFlightPoint", out var point)) flightPoint = Math.Clamp(point, 0, 11);
        if (tag.TryGetTagValue<float>("ObsidianSittingDamage", out var sitting) && float.IsFinite(sitting)) sittingDamage = Math.Max(0, sitting);
        if (EntityNbt.TryReadVector(tag, "ObsidianDestination", out var savedDestination)) destination = savedDestination;
        if (phase == DragonPhase.Dying && tag.TryGetTagValue<int>("DragonDeathTime", out var deathTime) && deathTime > 0) Health = 0;
    }
}

internal sealed class EnderDragonPart : Entity
{
    internal EnderDragon Parent { get; }
    internal int Index { get; }
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    internal EnderDragonPart(EnderDragon parent, int index, EntityDimension dimension)
    {
        Parent = parent;
        Index = index;
        Level = parent.Level;
        EntityId = parent.EntityId + index + 1;
        Dimension = dimension;
        Type = EntityType.EnderDragon;
    }
    internal void SetPosition(VectorD position) { Position = position; BoundingBox = Dimension.CreateBBFromPosition(position); }
    public override ValueTask DamageAsync(IEntity source, float amount = 1) => Parent.DamagePartAsync(source, amount, Index);
}


