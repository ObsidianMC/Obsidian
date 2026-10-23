using Obsidian.Entities;
using System.Threading;

namespace Obsidian.WorldData;

/// <summary>
/// A level's non-player entities by the chunk they're in, so a range query looks at the chunks around it rather than
/// at every entity of the regions it touches, and by UUID, like vanilla's entity lookup, so finding one (a projectile's
/// owner, a duplicate being loaded) doesn't go over them all. Players are left out: the level finds them through its
/// players.
/// </summary>
/// <remarks>
/// <para>
/// An entity joins when the level adds it and leaves when the level removes, evicts or unloads it, which are the only
/// ways into and out of its regions' entities (<see cref="IRegion.Entities"/> is read-only). It moves between chunks as
/// its position is set (see <see cref="Entity.Position"/>), and stays put when it only moves between regions.
/// </para>
/// <para>
/// Positions can be set off the level's tick (a client steering its vehicle), so everything is under one lock, and a
/// query copies what it found before returning it, letting its caller move or remove entities as it goes.
/// </para>
/// </remarks>
internal sealed class EntityGrid
{
    private readonly Lock gate = new();
    private readonly Dictionary<long, List<Entity>> chunks = [];
    private readonly Dictionary<Guid, Entity> byUuid = [];

    internal static long ChunkKey(VectorD position) =>
        NumericsHelper.IntsToLong((int)Math.Floor(position.X) >> 4, (int)Math.Floor(position.Z) >> 4);

    /// <summary>
    /// Adds the entity at its position, taking it out of another level's grid first.
    /// </summary>
    /// <returns>Whether it's in the grid now: not when another entity in it has its UUID.</returns>
    public bool Add(IEntity entity)
    {
        if (entity is not Entity concrete || entity is IPlayer)
            return true;

        if (concrete.Grid is EntityGrid previous && previous != this)
            previous.Remove(concrete);

        lock (this.gate)
        {
            if (concrete.Grid == this)
                return true;

            if (!this.byUuid.TryAdd(concrete.Uuid, concrete))
                return false;

            var key = ChunkKey(concrete.Position);
            this.Bucket(key).Add(concrete);
            concrete.Grid = this;
            concrete.GridChunk = key;
            return true;
        }
    }

    /// <summary>The entity in the grid with <paramref name="uuid"/>, if any.</summary>
    public Entity? WithUuid(Guid uuid)
    {
        lock (this.gate)
            return this.byUuid.GetValueOrDefault(uuid);
    }

    /// <summary>
    /// Takes the entity out of the grid it's in, if any.
    /// </summary>
    public static void Leave(IEntity entity)
    {
        if (entity is Entity { Grid: EntityGrid grid } concrete)
            grid.Remove(concrete);
    }

    private void Remove(Entity entity)
    {
        lock (this.gate)
        {
            if (entity.Grid != this)
                return;

            this.Unlink(entity, entity.GridChunk);
            this.byUuid.Remove(entity.Uuid);
            entity.Grid = null;
        }
    }

    /// <summary>
    /// Moves the entity to the chunk of <paramref name="position"/>, its new position.
    /// </summary>
    public void Move(Entity entity, VectorD position)
    {
        var key = ChunkKey(position);
        lock (this.gate)
        {
            if (entity.Grid != this || entity.GridChunk == key)
                return;

            this.Unlink(entity, entity.GridChunk);
            this.Bucket(key).Add(entity);
            entity.GridChunk = key;
        }
    }

    /// <summary>
    /// The entities other than players within <paramref name="distance"/> of <paramref name="center"/> that
    /// <paramref name="filter"/> accepts (all without one), by entity id. The filter runs outside the grid's lock.
    /// </summary>
    /// <remarks>
    /// Only the entities in range are kept and sorted: mobs' AI and players' tracking ask this every tick, and copying
    /// and sorting every entity in the chunks around cost more than the checks.
    /// </remarks>
    public Entity[] NonPlayersInRange(VectorD center, double distance, Func<IEntity, bool>? filter = null)
    {
        var (minX, minZ) = ((int)Math.Floor(center.X - distance) >> 4, (int)Math.Floor(center.Z - distance) >> 4);
        var (maxX, maxZ) = ((int)Math.Floor(center.X + distance) >> 4, (int)Math.Floor(center.Z + distance) >> 4);
        var distanceSquared = distance * distance;

        List<Entity>? found = null;
        lock (this.gate)
        {
            for (var x = minX; x <= maxX; x++)
            {
                for (var z = minZ; z <= maxZ; z++)
                {
                    if (!this.chunks.TryGetValue(NumericsHelper.IntsToLong(x, z), out var bucket))
                        continue;

                    foreach (var entity in bucket)
                    {
                        if (entity.Type != EntityType.Player && (entity.Position - center).MagnitudeSquared() <= distanceSquared)
                            (found ??= []).Add(entity);
                    }
                }
            }
        }

        if (filter is not null)
            found?.RemoveAll(entity => !filter(entity));

        if (found is not { Count: > 0 })
            return [];

        // A stable order whichever chunks the entities are in, for callers that take the first match.
        found.Sort(static (left, right) => left.EntityId.CompareTo(right.EntityId));
        return [.. found];
    }

    // Called under the gate.
    private List<Entity> Bucket(long key)
    {
        if (!this.chunks.TryGetValue(key, out var bucket))
            this.chunks[key] = bucket = [];

        return bucket;
    }

    // Called under the gate. Empty chunks are dropped, so the grid doesn't grow with every chunk entities pass through.
    private void Unlink(Entity entity, long key)
    {
        if (!this.chunks.TryGetValue(key, out var bucket))
            return;

        // By reference: entities equal by id can be different instances.
        for (var index = 0; index < bucket.Count; index++)
        {
            if (ReferenceEquals(bucket[index], entity))
            {
                bucket.RemoveAt(index);
                break;
            }
        }

        if (bucket.Count == 0)
            this.chunks.Remove(key);
    }
}
