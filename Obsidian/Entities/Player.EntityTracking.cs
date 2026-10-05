using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

public partial class Player
{
    internal ConcurrentDictionary<int, Guid> TrackedEntities { get; } = [];

    internal async ValueTask SynchronizeTrackedEntitiesAsync()
    {
        var visible = new HashSet<int>();
        foreach (var entity in Level.GetNonPlayerEntitiesInRange(Position, Server.Configuration.EntityBroadcastRangePercentage).OfType<Entity>())
        {
            var (x, z) = entity.Position.ToChunkCoord();
            if (!LoadedChunks.Contains(NumericsHelper.IntsToLong(x, z)) || entity is Mob { IsRemoved: true })
                continue;
            visible.Add(entity.EntityId);
            if (TrackedEntities.TryGetValue(entity.EntityId, out var uuid) && uuid == entity.Uuid)
                continue;
            var data = entity is Arrow arrow ? arrow.Owner?.EntityId ?? 0 : 0;
            await Client.QueuePacketAsync(entity.CreateSpawnPacket(new Velocity(entity.Motion.X, entity.Motion.Y, entity.Motion.Z), data));
            if (entity is Mob mob)
                await mob.SendEquipmentToAsync(this);
            if (entity is Living living)
                foreach (var (id, effect) in living.ActivePotionEffects)
                    await Client.QueuePacketAsync(new UpdateMobEffectPacket(entity.EntityId, id, effect.CurrentDuration)
                    { Amplifier = effect.EffectData.Amplifier, Flags = EntityEffectFlags.ShowParticles | EntityEffectFlags.ShowIcon });
            TrackedEntities[entity.EntityId] = entity.Uuid;
        }
        var removed = TrackedEntities.Keys.Where(id => !visible.Contains(id)).ToArray();
        if (removed.Length == 0)
            return;
        foreach (var id in removed)
            TrackedEntities.TryRemove(id, out _);
        await Client.QueuePacketAsync(new RemoveEntitiesPacket(removed));
    }
}
