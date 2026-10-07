namespace Obsidian.Entities;

using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;
using Obsidian.Nbt;

[MinecraftEntity("minecraft:arrow")]
public partial class Arrow : Entity
{
    public bool Crit { get; private set; }
    public bool NoClip { get; private set; }
    internal IEntity? Owner { get; set; }
    internal float Damage { get; set; } = 2;
    internal int Effect { get; set; } = -1;
    internal int EffectDuration { get; set; }
    internal int FireSeconds { get; set; }
    private Guid ownerUuid;
    private int age;
    private bool embedded;

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        if (Type is EntityType.ShulkerBullet or EntityType.WitherSkull) return;
        writer.WriteEntityMetadataType(8, EntityMetadataType.Byte);
        writer.WriteByte((byte)((Crit ? 1 : 0) | (NoClip ? 2 : 0)));
        writer.WriteEntityMetadataType(9, EntityMetadataType.Byte);
        writer.WriteByte((byte)0);
        writer.WriteEntityMetadataType(10, EntityMetadataType.Boolean);
        writer.WriteBoolean(embedded);
        if (Type != EntityType.Trident)
        {
            writer.WriteEntityMetadataType(11, EntityMetadataType.VarInt);
            writer.WriteVarInt(Effect switch { 1 => 0x5A6C81, 18 => 0x4E9331, 17 => 0x484D48, _ => -1 });
        }
    }

    public override void SpawnEntity(Velocity? velocity = null, int additionalData = 0)
    {
        base.SpawnEntity(velocity ?? new Velocity(Motion.X, Motion.Y, Motion.Z), Owner?.EntityId ?? 0);
        if (Level is AbstractLevel level) level.EmitGameEvent(MobGameEvent.ProjectileShoot, Position, Owner ?? this);
    }

    public override async ValueTask TickAsync()
    {
        if (Level is not AbstractLevel level || !level.IsMobTicking(Position))
            return;
        if (++age >= 1200 || Position.Y < -128)
        {
            await RemoveAsync();
            return;
        }
        if (Owner == null && ownerUuid != Guid.Empty && age % 20 == 1)
            Owner = Level.GetEntitiesInRange(Position, float.MaxValue).FirstOrDefault(entity => entity.Uuid == ownerUuid);
        if (embedded)
            return;
        var terrain = new MobTerrain(Level);
        var end = Position + Motion;
        var bounds = new BoundingBox(VectorD.Min(Position, end) - new VectorD(0.3f), VectorD.Max(Position, end) + new VectorD(0.3f));
        var fraction = 1d;
        foreach (var shape in terrain.GetCollisions(bounds))
            if (MobTerrain.RayIntersection(shape, Position, Motion) is double hit)
                fraction = Math.Min(fraction, hit);
        IEntity? target = null;
        foreach (var candidate in Level.GetEntitiesInRange(Position, (float)Motion.Magnitude + 20))
        {
            if (candidate is not Living and not EndCrystal and not MobProjectile { Type: EntityType.Fireball } || ReferenceEquals(candidate, Owner) || candidate.Health <= 0 ||
                candidate is IPlayer player && player.GameMode is GameMode.Creative or GameMode.Spectator)
                continue;
            if (candidate is EnderDragon dragon)
            {
                foreach (var part in dragon.Parts)
                    if (MobTerrain.RayIntersection(part.BoundingBox, Position, Motion) is double partHit && partHit < fraction)
                    { fraction = partHit; target = part; }
                continue;
            }
            var targetBounds = candidate.Dimension.CreateBBFromPosition(candidate.Position);
            targetBounds = new BoundingBox(targetBounds.Min - new VectorD(0.3f), targetBounds.Max + new VectorD(0.3f));
            if (MobTerrain.RayIntersection(targetBounds, Position, Motion) is double hit && hit < fraction)
            {
                if (candidate is Enderman enderman)
                {
                    enderman.TryAvoidProjectile();
                    continue;
                }
                fraction = hit;
                target = candidate;
            }
        }
        var next = Position + Motion * fraction;
        if (!level.TryMoveEntity(this, Position, next))
            return;
        Position = next;
        Yaw = (float)(Math.Atan2(Motion.X, Motion.Z) * 180 / Math.PI);
        Pitch = (float)(Math.Atan2(Motion.Y, Math.Sqrt(Motion.X * Motion.X + Motion.Z * Motion.Z)) * 180 / Math.PI);
        PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new TeleportEntityPacket
        { EntityId = EntityId, Position = Position, Delta = Motion, Yaw = Yaw, Pitch = Pitch, OnGround = false });
        if (target != null)
        {
            level.EmitGameEvent(MobGameEvent.ProjectileLand, Position, this, Owner);
            var health = target.Health;
            if (target is MobProjectile fireball)
                fireball.Deflect(Owner ?? this, Motion);
            else if ((target is not Shulker { Closed: true } || Type != EntityType.Arrow) &&
                (target is not Wither { Armored: true } || Type is not (EntityType.Arrow or EntityType.Trident or EntityType.SpectralArrow)))
            {
                var damage = Type == EntityType.ShulkerBullet ? 4 : Type == EntityType.WitherSkull ? Owner is Living ? 8 : 5 :
                    Type == EntityType.Trident ? 8 : (float)Math.Ceiling(Motion.Magnitude * Damage);
                if (target is EnderDragonPart part) await part.Parent.DamagePartAsync(Owner ?? this, damage, part.Index, projectile: true);
                else await target.DamageAsync(Owner ?? this, damage);
            }
            if (target.Health < health && Effect >= 0 && target is Living living)
                living.AddPotionEffect(Effect, EffectDuration, 0);
            if (target.Health < health && FireSeconds > 0 && target is Living burningTarget)
                burningTarget.Ignite(FireSeconds);
            await OnImpactAsync(target, target.Health < health);
            return;
        }
        if (fraction < 1)
        {
            level.EmitGameEvent(MobGameEvent.ProjectileLand, Position, this, Owner);
            if (Type is EntityType.ShulkerBullet or EntityType.WitherSkull) { await OnImpactAsync(null, false); return; }
            embedded = true;
            Motion = VectorD.Zero;
            PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new SetEntityDataPacket { EntityId = EntityId, Entity = this });
            return;
        }
        var water = terrain.GetBlock((Vector)Position.Floor())?.Material == Material.Water;
        Motion *= GetDrag(water);
        if (!NoGravity)
            Motion -= new VectorD(0, 0.05f, 0);
    }

    protected virtual ValueTask OnImpactAsync(IEntity? target, bool damaged) => RemoveAsync();
    protected virtual double GetDrag(bool water) => water ? 0.6 : 0.99;

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);
        var uuid = Owner?.Uuid ?? ownerUuid;
        tag.SetOrRemove("Owner", uuid == Guid.Empty ? null : new NbtArray<int>("Owner", EntityNbt.UuidToInts(uuid)));
        tag.Set(new NbtTag<int>("ObsidianLife", age));
        tag.Set(new NbtTag<bool>("inGround", embedded));
        tag.Set(new NbtTag<double>("damage", Damage));
        tag.Set(new NbtTag<int>("ObsidianEffect", Effect));
        tag.Set(new NbtTag<int>("ObsidianEffectDuration", EffectDuration));
        tag.Set(new NbtTag<int>("ObsidianFireSeconds", FireSeconds));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);
        if (tag.TryGetTag<NbtArray<int>>("Owner", out var owner) && owner.Count == 4)
            ownerUuid = EntityNbt.UuidFromInts(owner.GetArray());
        if (tag.TryGetTagValue<int>("ObsidianLife", out var life)) age = Math.Clamp(life, 0, 1200);
        embedded = tag.TryGetBool("inGround", out var ground) && ground;
        if (tag.TryGetTagValue<double>("damage", out var damage) && double.IsFinite(damage)) Damage = (float)Math.Clamp(damage, 0, float.MaxValue);
        if (tag.TryGetTagValue<int>("ObsidianEffect", out var effect)) Effect = effect;
        if (tag.TryGetTagValue<int>("ObsidianEffectDuration", out var duration)) EffectDuration = Math.Max(0, duration);
        if (tag.TryGetTagValue<int>("ObsidianFireSeconds", out var fire)) FireSeconds = Math.Clamp(fire, 0, int.MaxValue / 20);
        BoundingBox = Dimension.CreateBBFromPosition(Position);
    }
}
