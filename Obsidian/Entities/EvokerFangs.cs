using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:evoker_fangs")]
public sealed partial class EvokerFangs : Entity
{
    internal int Warmup { get; set; }
    internal Guid OwnerUuid { get; set; }
    private bool started;
    private int life = 22;
    public EvokerFangs() => Type = EntityType.EvokerFangs;
    public override async ValueTask TickAsync()
    {
        if (--Warmup >= 0) return;
        if (!started)
        {
            started = true;
            PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new EntityEventPacket { EntityId = EntityId, Event = 4 });
        }
        if (Warmup == -8)
        {
            var owner = Level.GetEntitiesInRange(Position, 64).FirstOrDefault(entity => entity.Uuid == OwnerUuid);
            var bounds = Dimension.CreateBBFromPosition(Position);
            foreach (var target in Level.GetEntitiesInRange(Position, 2).OfType<Living>().Where(target => target.Uuid != OwnerUuid &&
                target.Health > 0 && target.Type is not (EntityType.Evoker or EntityType.Vindicator or EntityType.Pillager or EntityType.Illusioner) &&
                bounds.Intersects(target.Dimension.CreateBBFromPosition(target.Position))))
                await target.DamageAsync(owner ?? this, 6);
        }
        if (--life < 0) await RemoveAsync();
    }
    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);
        tag.Set(new NbtTag<int>("Warmup", Warmup));
        if (OwnerUuid != Guid.Empty) tag.Set(new NbtArray<int>("Owner", EntityNbt.UuidToInts(OwnerUuid)));
    }
    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);
        Warmup = Math.Clamp(tag.TryGetTagValue<int>("Warmup", out var warmup) ? warmup : 0, -30, 1000);
        life = Warmup < 0 ? Math.Max(0, 22 + Warmup) : 22;
        if (tag.TryGetTag<NbtArray<int>>("Owner", out var owner) && owner.Count == 4) OwnerUuid = EntityNbt.UuidFromInts(owner.GetArray());
    }
}
