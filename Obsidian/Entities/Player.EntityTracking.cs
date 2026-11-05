using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

public partial class Player
{
    internal ConcurrentDictionary<int, Guid> TrackedEntities { get; } = [];

    // Explicit, because the hooks are internal to IPlayer and fall back to its no-op defaults if left unimplemented.
    void IPlayer.TrackEntity(IEntity entity) => TrackedEntities[entity.EntityId] = entity.Uuid;

    void IPlayer.UntrackEntity(int entityId) => TrackedEntities.TryRemove(entityId, out _);

    void IPlayer.ForgetVisiblePlayer(IPlayer player) => this.visiblePlayers.Remove(player);

    ValueTask IPlayer.SynchronizeTrackedEntitiesAsync(ILevel level) => this.SynchronizeTrackedEntitiesAsync(level);

    /// <summary>
    /// The tracking pass of <paramref name="level"/>'s tick: the client is shown the entities around the player in it,
    /// and forgets those out of range, as long as the player is still in that level.
    /// </summary>
    /// <remarks>
    /// Under chunkUpdates, which a level change, respawn or teleport holds while it moves the player and resets and
    /// tracks the client's view where they end up. So the pass of a level the player left (another level's tick may
    /// have moved them, through a portal or an ender pearl) neither shows the client its entities nor makes it forget
    /// those of the player's new level. It leaves them to that level, whose entities it mustn't read during its tick.
    /// </remarks>
    internal async ValueTask SynchronizeTrackedEntitiesAsync(ILevel level)
    {
        await this.chunkUpdates.WaitAsync();
        try
        {
            if (ReferenceEquals(this.Level, level))
                await this.SynchronizeTrackedEntitiesUnderLockAsync();
        }
        finally
        {
            this.chunkUpdates.Release();
        }
    }

    // Tracks the entities around the player in their level, with chunkUpdates held.
    private protected override async ValueTask SynchronizeTrackedEntitiesUnderLockAsync()
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
            {
                await mob.SendEquipmentToAsync(this);
                await mob.SendLeashToAsync(this);
            }

            // Like vanilla's pairing data: who rides the entity, and the seats of the vehicle it rides.
            await entity.SendPassengersToAsync(this);
            if (entity.Vehicle is Entity vehicle)
                await vehicle.SendPassengersToAsync(this);

            if (entity is Living living)
            {
                foreach (var (id, effect) in living.ActivePotionEffects)
                {
                    await Client.QueuePacketAsync(new UpdateMobEffectPacket(entity.EntityId, id, effect.CurrentDuration)
                    { Amplifier = effect.EffectData.Amplifier, Flags = PotionEffectFlags(effect.EffectData) });
                }
            }

            TrackedEntities[entity.EntityId] = entity.Uuid;
        }
        // Read in place: copying the keys takes all the dictionary's locks.
        List<int>? removed = null;
        foreach (var (id, _) in TrackedEntities)
        {
            if (!visible.Contains(id))
                (removed ??= []).Add(id);
        }
        if (removed is null)
            return;
        foreach (var id in removed)
            TrackedEntities.TryRemove(id, out _);
        await Client.QueuePacketAsync(new RemoveEntitiesPacket([.. removed]));
    }
}
