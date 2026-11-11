using Obsidian.Entities;

namespace Obsidian.WorldData.TrialSpawners;

/// <summary>
/// A set of UUIDs that iterates in the order a <c>java.util.HashSet</c> of them does, as a trial spawner's players and
/// mobs do in vanilla. Immutable; each change returns a new set.
/// </summary>
/// <remarks>
/// The order has to match where vanilla acts on it: an ejection rewards and lets go of the first player in it, which
/// its saved data shows, and an ominous trial picks one of them at random by position in it. A hash set iterates by
/// bucket (the UUID's hash spread over its table, which starts at 16 and doubles past three quarters full), then by
/// when each joined its bucket. Clearing a hash set keeps its table, so the set keeps its capacity.
/// </remarks>
internal sealed record UuidSet
{
    private const int InitialCapacity = 16;

    public static UuidSet Empty { get; } = new();

    /// <summary>The UUIDs in the order they were added, which is how they're saved.</summary>
    public ImmutableArray<Guid> Members { get; private init; } = [];

    // The table size of vanilla's hash set, which only grows.
    private int Capacity { get; init; } = InitialCapacity;

    public int Count => this.Members.Length;

    public bool IsEmpty => this.Members.IsEmpty;

    /// <summary>The UUIDs in the order vanilla's hash set iterates them.</summary>
    public IEnumerable<Guid> InIterationOrder => this.Members
        .Select((uuid, index) => (Uuid: uuid, Index: index))
        .OrderBy(entry => this.Bucket(entry.Uuid))
        .ThenBy(entry => entry.Index)
        .Select(entry => entry.Uuid);

    public bool Contains(Guid uuid) => this.Members.Contains(uuid);

    /// <summary>
    /// A set of <paramref name="uuids"/> added in order, as vanilla loads a saved set into a new one.
    /// </summary>
    public static UuidSet Of(IEnumerable<Guid> uuids) => Empty.AddRange(uuids);

    /// <summary>The set with <paramref name="uuids"/> added in order; the same set when none was new.</summary>
    public UuidSet AddRange(IEnumerable<Guid> uuids)
    {
        var members = this.Members;
        var capacity = this.Capacity;
        foreach (var uuid in uuids)
        {
            if (members.Contains(uuid))
                continue;

            members = members.Add(uuid);

            // Vanilla's HashMap resizes once its size passes three quarters of its table.
            if (members.Length > capacity * 3 / 4)
                capacity *= 2;
        }

        return members.Length == this.Members.Length ? this : this with { Members = members, Capacity = capacity };
    }

    public UuidSet Add(Guid uuid) => this.AddRange([uuid]);

    /// <summary>The set without the UUIDs <paramref name="remove"/> picks; the same set when it picks none.</summary>
    public UuidSet RemoveAll(Predicate<Guid> remove)
    {
        var kept = this.Members.RemoveAll(remove);
        return kept.Length == this.Members.Length ? this : this with { Members = kept };
    }

    /// <summary>The set without the first UUID it iterates.</summary>
    public UuidSet RemoveFirst()
    {
        if (this.IsEmpty)
            return this;

        var first = this.InIterationOrder.First();
        return this with { Members = this.Members.Remove(first) };
    }

    /// <summary>The set emptied, which keeps its table as a cleared hash set does.</summary>
    public UuidSet Clear() => this.IsEmpty ? this : this with { Members = [] };

    // Java's UUID.hashCode, spread as HashMap spreads it, masked to the table.
    private int Bucket(Guid uuid)
    {
        var ints = EntityNbt.UuidToInts(uuid);
        var most = (long)ints[0] << 32 | (uint)ints[1];
        var least = (long)ints[2] << 32 | (uint)ints[3];
        var bits = most ^ least;
        var hash = (int)(bits >> 32) ^ (int)bits;
        var spread = hash ^ (int)((uint)hash >> 16);
        return spread & (this.Capacity - 1);
    }
}
