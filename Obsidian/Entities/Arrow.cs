namespace Obsidian.Entities;

using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

[MinecraftEntity("minecraft:arrow")]
public partial class Arrow : Entity
{
    public bool Crit { get; private set; }
    public bool NoClip { get; private set; }
    internal IEntity? Owner { get; init; }
    internal float Damage { get; init; } = 2;
    internal int Effect { get; init; } = -1;
    internal int EffectDuration { get; init; }
    private int age;
    private bool embedded;

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(8, EntityMetadataType.Byte);
        writer.WriteByte((byte)((Crit ? 1 : 0) | (NoClip ? 2 : 0)));
        writer.WriteEntityMetadataType(9, EntityMetadataType.Byte);
        writer.WriteByte((byte)0);
        writer.WriteEntityMetadataType(10, EntityMetadataType.Boolean);
        writer.WriteBoolean(embedded);
        writer.WriteEntityMetadataType(11, EntityMetadataType.VarInt);
        writer.WriteVarInt(Effect switch { 1 => 0x5A6C81, 18 => 0x4E9331, 17 => 0x484D48, _ => -1 });
    }

    public override void SpawnEntity(Velocity? velocity = null, int additionalData = 0) =>
        base.SpawnEntity(velocity ?? new Velocity(Motion.X, Motion.Y, Motion.Z), Owner?.EntityId ?? 0);

    public override async ValueTask TickAsync()
    {
        if (Level is not AbstractLevel level || !level.IsMobTicking(Position))
            return;
        if (++age >= 1200 || Position.Y < -128)
        {
            await RemoveAsync();
            return;
        }
        if (embedded)
            return;
        var terrain = new MobTerrain(Level);
        var end = Position + Motion;
        var bounds = new BoundingBox(VectorF.Min(Position, end) - new VectorF(0.3f), VectorF.Max(Position, end) + new VectorF(0.3f));
        var fraction = 1f;
        foreach (var shape in terrain.GetCollisions(bounds))
            if (MobTerrain.RayIntersection(shape, Position, Motion) is float hit)
                fraction = Math.Min(fraction, hit);
        IEntity? target = null;
        foreach (var candidate in Level.GetEntitiesInRange(Position, Motion.Magnitude + 2))
        {
            if (candidate is not Living || ReferenceEquals(candidate, Owner) || candidate.Health <= 0 ||
                candidate is IPlayer player && player.Gamemode is Gamemode.Creative or Gamemode.Spectator)
                continue;
            var targetBounds = candidate.Dimension.CreateBBFromPosition(candidate.Position);
            targetBounds = new BoundingBox(targetBounds.Min - new VectorF(0.3f), targetBounds.Max + new VectorF(0.3f));
            if (MobTerrain.RayIntersection(targetBounds, Position, Motion) is float hit && hit < fraction)
            {
                fraction = hit;
                target = candidate;
            }
        }
        var next = Position + Motion * fraction;
        if (!level.TryMoveEntity(this, Position, next))
            return;
        Position = next;
        Yaw = MathF.Atan2(Motion.X, Motion.Z) * 180 / MathF.PI;
        Pitch = MathF.Atan2(Motion.Y, MathF.Sqrt(Motion.X * Motion.X + Motion.Z * Motion.Z)) * 180 / MathF.PI;
        PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new TeleportEntityPacket
        { EntityId = EntityId, Position = Position, Yaw = Yaw, Pitch = Pitch, OnGround = false });
        if (target != null)
        {
            var health = target.Health;
            await target.DamageAsync(Owner ?? this, MathF.Ceiling(Motion.Magnitude * Damage));
            if (target.Health < health && Effect >= 0 && target is Living living)
                living.AddPotionEffect(Effect, EffectDuration, 0);
            await RemoveAsync();
            return;
        }
        if (fraction < 1)
        {
            embedded = true;
            Motion = VectorF.Zero;
            PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new SetEntityDataPacket { EntityId = EntityId, Entity = this });
            return;
        }
        var water = terrain.GetBlock((Vector)Position.Floor())?.Material == Material.Water;
        Motion *= water ? 0.6f : 0.99f;
        if (!NoGravity)
            Motion -= new VectorF(0, 0.05f, 0);
    }
}
