namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    private readonly ConcurrentDictionary<Vector, Guid> villagePois = [];
    internal bool TryClaimVillagePoi(Vector position, Guid owner) => villagePois.TryAdd(position, owner) ||
        villagePois.TryGetValue(position, out var existing) && existing == owner;
    internal void ReleaseVillagePoi(Vector position, Guid owner) => villagePois.TryRemove(new KeyValuePair<Vector, Guid>(position, owner));
}
