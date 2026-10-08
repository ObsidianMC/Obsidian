using Obsidian.API.Inventory;

namespace Obsidian.Entities;

using Obsidian.API.Inventory.DataComponents;
using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Nbt;

[MinecraftEntity("minecraft:firework_rocket")]
public sealed partial class FireworkRocket : Entity
{
    public ItemStack? Item { get; internal set; }
    public int Rotation { get; private set; }
    internal IEntity? Owner { get; set; }
    internal bool ShotAtAngle { get; set; }
    private int life;
    private int lifetime;

    public override async ValueTask TickAsync()
    {
        if (Level is not Obsidian.WorldData.AbstractLevel level || !level.IsMobTicking(Position)) return;
        var fireworks = Item?.GetComponent<FireworksDataComponent>(DataComponentType.Fireworks);
        if (lifetime == 0) lifetime = 10 * (1 + (fireworks?.FlightDuration ?? 0)) + Globals.Random.Next(6) + Globals.Random.Next(7);
        if (!ShotAtAngle) Motion = new VectorD(Motion.X * 1.15, Motion.Y + 0.04, Motion.Z * 1.15);
        var terrain = new MobTerrain(Level);
        var end = Position + Motion;
        var bounds = new BoundingBox(VectorD.Min(Position, end) - new VectorD(0.1), VectorD.Max(Position, end) + new VectorD(0.1));
        var collided = terrain.GetCollisions(bounds).Any(shape => MobTerrain.RayIntersection(shape, Position, Motion) != null);
        var target = Level.GetEntitiesInRange(Position, (float)Motion.Magnitude + 2).OfType<Living>()
            .FirstOrDefault(entity => !ReferenceEquals(entity, Owner) && MobTerrain.RayIntersection(entity.Dimension.CreateBBFromPosition(entity.Position), Position, Motion) != null);
        if (!level.TryMoveEntity(this, Position, end)) return;
        Position = end;
        if (++life > lifetime || collided || target != null)
        {
            if (fireworks is { Explosions.Length: > 0 })
            {
                var damage = 5 + fireworks.Explosions.Length * 2;
                foreach (var entity in Level.GetEntitiesInRange(Position, 5).OfType<Living>())
                {
                    var distance = (entity.Position - Position).Magnitude;
                    if (distance < 5 && terrain.HasLineOfSight(Position, entity.Position + new VectorD(0, entity.Dimension.Height / 2, 0)))
                        await entity.DamageCombatAsync(Owner ?? this, ReferenceEquals(entity, target) ? damage : (float)(damage * Math.Sqrt((5 - distance) / 5)), CombatDamageKind.Explosion);
                }
            }
            PacketBroadcaster.QueuePacketToLevel(Level, new EntityEventPacket { EntityId = EntityId, Event = 17 });
            await RemoveAsync();
            return;
        }
        PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new TeleportEntityPacket
        { EntityId = EntityId, Position = Position, Delta = Motion, Yaw = Yaw, Pitch = Pitch, OnGround = false });
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(8, EntityMetadataType.Slot);
        writer.WriteItemStack(Item);
        writer.WriteEntityMetadataType(9, EntityMetadataType.OptionalUnsignedVarInt);
        writer.WriteVarInt(0);
        writer.WriteEntityMetadataType(10, EntityMetadataType.Boolean);
        writer.WriteBoolean(ShotAtAngle);
    }

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);
        tag.Set(new NbtTag<int>("Life", life));
        tag.Set(new NbtTag<int>("LifeTime", lifetime));
        tag.Set(new NbtTag<bool>("ShotAtAngle", ShotAtAngle));
        tag.SetOrRemove("FireworksItem", Item?.ToNbt("FireworksItem"));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);
        life = tag.TryGetTagValue<int>("Life", out var savedLife) ? Math.Max(0, savedLife) : 0;
        lifetime = tag.TryGetTagValue<int>("LifeTime", out var savedLifetime) ? Math.Max(0, savedLifetime) : 0;
        ShotAtAngle = tag.TryGetBool("ShotAtAngle", out var shot) && shot;
        Item = tag.TryGetTag<NbtCompound>("FireworksItem", out var item) ? item.ItemFromNbt() : null;
    }
}
