using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:ender_dragon")]
public sealed partial class EnderDragon : BossMob
{
    private enum DragonPhase { Holding, Strafing, LandingApproach, Landing, Takeoff, Flaming, Scanning, Attacking, Charging, Dying, Hovering }
    private DragonPhase phase;
    private int phaseTicks;
    private int flameCount;
    private int flightPoint;
    private long lastDamageTick = -100;
    private float lastDamage;
    private float sittingDamage;
    private VectorD destination;
    private VectorD arena;
    private bool arenaKnown;
    private EndCrystal? crystal;
    private DragonBreathCloud? breath;
    private EnderDragonPart[] parts = [];
    private readonly Dictionary<int, long> contactTicks = [];
    public bool PreviouslyKilled { get; internal set; } = true;
    internal bool IsPerched => phase is DragonPhase.Flaming or DragonPhase.Scanning or DragonPhase.Attacking;
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
    protected override bool CanTakeDamage(IEntity source) => source is IPlayer or EndCrystal;
    internal override bool Hostile => false;
    protected override bool CanDespawn => false;
    protected override bool TakesFallDamage => false;
    protected override ValueTask TickAirSupplyAsync() => default;
    protected override string? SoundName => "ender_dragon";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;

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
        var forward = new VectorD(-Math.Sin(angle), 0, Math.Cos(angle));
        var side = new VectorD(Math.Cos(angle), 0, Math.Sin(angle));
        for (var index = 0; index < parts.Length; index++)
        {
            var offset = index switch
            {
                0 => forward * 6.5f + new VectorD(0, IsPerched ? 1 : 2, 0),
                1 => forward * 5.5f + new VectorD(0, 2, 0),
                2 => forward * 0.5f,
                3 => forward * -3.5f + new VectorD(0, 1.5f, 0),
                4 => forward * -5.5f + new VectorD(0, 1.5f, 0),
                5 => forward * -7.5f + new VectorD(0, 1.5f, 0),
                6 => side * 4.5f + new VectorD(0, 2, 0),
                _ => side * -4.5f + new VectorD(0, 2, 0)
            };
            parts[index].SetPosition(Position + offset);
        }
    }

    protected override VectorD Travel()
    {
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return Position;
        return Position + Motion;
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
            SelectFlightPoint();
        }
        UpdateParts();
        phaseTicks++;
        if (AiTick % 10 == 0 && crystal is { Health: > 0 })
        {
            Health = Math.Min(GetAttributeValue("minecraft:generic.max_health"), Health + 1);
            SynchronizeMetadata();
        }
        if (AiTick % 20 == 0)
            crystal = Level.GetEntitiesInRange(Position, 32).OfType<EndCrystal>().Where(item => item.Health > 0)
                .MinBy(item => (item.Position - Position).MagnitudeSquared());
        AttackTarget = Level.GetPlayersInRange(Position, 128).Where(IsValidTarget)
            .MinBy(player => (player.Position - Position).MagnitudeSquared());
        switch (phase)
        {
            case DragonPhase.Holding:
            case DragonPhase.Hovering:
                if ((destination - Position).MagnitudeSquared() < 100 || phaseTicks > 200)
                {
                    var crystals = Level.GetEntitiesInRange(arena, 128).OfType<EndCrystal>().Count(item => item.Health > 0);
                    if (Random.Next(crystals + 3) == 0) SetPhase(DragonPhase.LandingApproach);
                    else if (AttackTarget != null && Random.Next(3) == 0) SetPhase(DragonPhase.Strafing);
                    else { SelectFlightPoint(); phaseTicks = 0; }
                }
                break;
            case DragonPhase.Strafing:
                if (AttackTarget is { } target)
                {
                    destination = target.Position + new VectorD(0, 15, 0);
                    var direction = target.Position + new VectorD(0, target.Dimension.Height / 2, 0) - parts[0].Position;
                    if (phaseTicks >= 5 && direction.MagnitudeSquared() < 4096 && CanSee(target))
                    {
                        var look = GetLookDirection();
                        if (direction.Magnitude > 0.001f && VectorD.Dot(direction / direction.Magnitude, look) > 0.95)
                        {
                            Level.SpawnEntity(new DragonFireball(this, parts[0].Position, direction));
                            PlayMobSound("shoot");
                            SetPhase(DragonPhase.Holding);
                        }
                    }
                }
                if (phaseTicks > 200 || AttackTarget == null) SetPhase(DragonPhase.Holding);
                break;
            case DragonPhase.LandingApproach:
                destination = arena + new VectorD(0, 30, 0);
                if ((destination - Position).MagnitudeSquared() < 100) SetPhase(DragonPhase.Landing);
                break;
            case DragonPhase.Landing:
                destination = arena;
                if ((destination - Position).MagnitudeSquared() < 1) { SetPhase(DragonPhase.Scanning); flameCount = 0; sittingDamage = 0; }
                break;
            case DragonPhase.Scanning:
                Motion = VectorD.Zero;
                if (AttackTarget != null) Face(AttackTarget.Position);
                if (phaseTicks >= 25 && AttackTarget is { } close && (close.Position - arena).MagnitudeSquared() < 400)
                    SetPhase(DragonPhase.Attacking);
                else if (phaseTicks >= 100) SetPhase(DragonPhase.Takeoff);
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
                    var origin = parts[0].Position + GetLookDirection() * 2.5f;
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
                destination = arena + GetLookDirection() * 40 + new VectorD(0, 30, 0);
                if ((Position - arena).MagnitudeSquared() > 100) SetPhase(DragonPhase.Holding);
                break;
            case DragonPhase.Charging:
                if ((destination - Position).MagnitudeSquared() < 100 || phaseTicks >= 100) SetPhase(DragonPhase.Holding);
                break;
        }
        if (!IsPerched)
        {
            Face(destination);
            var difference = destination - Position;
            var speed = phase == DragonPhase.Landing ? 0.6f : phase == DragonPhase.Charging ? 1.5f : 1f;
            if (difference.Magnitude > 0.01f)
                Motion = Motion * 0.8f + difference / difference.Magnitude * speed * 0.2f;
        }
        await ContactAttackAsync();
        await DestroyBlocksAsync();
    }

    private async ValueTask DestroyBlocksAsync()
    {
        if (Level is not AbstractLevel level || level.Generator is Obsidian.WorldData.Generators.MobTestGenerator)
            return;
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
                if (TagsRegistry.Block.DragonImmune.Entries.Contains(block.RegistryId)) blocked = true;
                else await level.SetBlockAsync(position, BlocksRegistry.Get(Material.Air), true);
            }
        }
        if (blocked) Motion *= 0.8f;
    }

    private void Face(VectorD target)
    {
        var desired = Math.Atan2(-(target.X - Position.X), target.Z - Position.Z) * 180 / Math.PI;
        var difference = (desired - Yaw.Degrees + 540) % 360 - 180;
        Yaw = (float)(Yaw.Degrees + Math.Clamp(difference, -10, 10));
    }
    private void SelectFlightPoint()
    {
        flightPoint = (flightPoint + Random.Next(1, 4)) % 12;
        var angle = flightPoint * Math.PI / 6;
        destination = arena + new VectorD(Math.Cos(angle) * 60, 25 + Random.Next(15), Math.Sin(angle) * 60);
    }
    private void SetPhase(DragonPhase next)
    {
        phase = next;
        phaseTicks = 0;
        if (next == DragonPhase.Holding) SelectFlightPoint();
        SynchronizeMetadata();
    }
    private async ValueTask ContactAttackAsync()
    {
        foreach (var target in Level.GetEntitiesInRange(Position, 16).OfType<Living>().ToArray())
        {
            if (ReferenceEquals(target, this) || !target.Alive || target is IPlayer { GameMode: GameMode.Creative or GameMode.Spectator }) continue;
            var bounds = target.Dimension.CreateBBFromPosition(target.Position);
            var head = parts[0].BoundingBox.Intersects(bounds) || parts[1].BoundingBox.Intersects(bounds);
            var wing = parts[6].BoundingBox.Intersects(bounds) || parts[7].BoundingBox.Intersects(bounds);
            if (!head && !wing || contactTicks.TryGetValue(target.EntityId, out var tick) && AiTick - tick < 10) continue;
            contactTicks[target.EntityId] = AiTick;
            await target.DamageAsync(this, head ? 10 : 5);
            if (wing && !IsPerched)
            {
                var offset = target.Position - Position;
                var divisor = Math.Max(0.1, offset.X * offset.X + offset.Z * offset.Z);
                target.Motion += new VectorD(offset.X / divisor * 4, 0.2f, offset.Z / divisor * 4);
            }
        }
        if (AiTick % 100 == 0)
            foreach (var id in contactTicks.Where(entry => AiTick - entry.Value > 100).Select(entry => entry.Key).ToArray()) contactTicks.Remove(id);
    }

    public override ValueTask DamageAsync(IEntity source, float amount = 1) => DamagePartAsync(source, amount, 0);
    internal async ValueTask DamagePartAsync(IEntity source, float amount, int part, bool projectile = false)
    {
        if (IsRemoved || phase == DragonPhase.Dying || source.Level != Level || !float.IsFinite(amount) || amount <= 0 ||
            source is not IPlayer && source is not EndCrystal || IsPerched && projectile) return;
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
        if (Health <= 0) await KillAsync(source);
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
        Motion = new VectorD(0, 0.1f, 0);
        var next = Position + Motion;
        if (Level is AbstractLevel level && level.TryMoveEntity(this, Position, next))
        {
            Position = next;
            BoundingBox = Dimension.CreateBBFromPosition(Position);
            PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new TeleportEntityPacket
            { EntityId = EntityId, Position = Position, Delta = Motion, Yaw = Yaw, Pitch = Pitch, OnGround = false });
        }
        if (phaseTicks > 150 && phaseTicks % 5 == 0)
            Level.SpawnExperienceOrbs(Position, (short)(PreviouslyKilled ? 40 : 960));
        if (phaseTicks >= 200)
        {
            Level.SpawnExperienceOrbs(Position, (short)(PreviouslyKilled ? 100 : 2400));
            if (Level is AbstractLevel fightLevel) await fightLevel.EndFightDragonKilledAsync(this);
            await RemoveAsync();
        }
    }
    internal async ValueTask OnCrystalDestroyedAsync(EndCrystal destroyed, IEntity source)
    {
        if (ReferenceEquals(crystal, destroyed))
        {
            crystal = null;
            await DamagePartAsync(destroyed, 10, 0);
        }
        if (!IsPerched && phase != DragonPhase.Dying && source is IPlayer)
        {
            destination = source.Position;
            SetPhase(DragonPhase.Charging);
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
        tag.Set(new NbtTag<int>("DragonPhase", (int)phase));
        tag.Set(new NbtTag<int>("DragonDeathTime", phase == DragonPhase.Dying ? phaseTicks : 0));
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
        phase = (DragonPhase)Math.Clamp(tag.TryGetTagValue<int>("DragonPhase", out var saved) ? saved : 0, 0, 10);
        phaseTicks = phase == DragonPhase.Dying && tag.TryGetTagValue<int>("DragonDeathTime", out var ticks) ? Math.Clamp(ticks, 0, 199) : 0;
        PreviouslyKilled = !tag.TryGetBool("ObsidianPreviouslyKilled", out var killed) || killed;
        arenaKnown = EntityNbt.TryReadVector(tag, "ObsidianDragonArena", out arena);
        if (arenaKnown) SelectFlightPoint();
        if (tag.TryGetTagValue<int>("ObsidianPhaseTicks", out var phaseTime)) phaseTicks = Math.Clamp(phaseTime, 0, 72000);
        if (tag.TryGetTagValue<int>("ObsidianFlameCount", out var flames)) flameCount = Math.Clamp(flames, 0, 4);
        if (tag.TryGetTagValue<int>("ObsidianFlightPoint", out var point)) flightPoint = Math.Clamp(point, 0, 11);
        if (tag.TryGetTagValue<float>("ObsidianSittingDamage", out var sitting) && float.IsFinite(sitting)) sittingDamage = Math.Max(0, sitting);
        if (EntityNbt.TryReadVector(tag, "ObsidianDestination", out var savedDestination)) destination = savedDestination;
        if (phase == DragonPhase.Dying) Health = 0;
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


