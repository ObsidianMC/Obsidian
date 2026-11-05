using System.Threading;

namespace Obsidian.WorldData;

/// <summary>
/// A level's entity transfer lock: what makes moving items and entities in, out of and around one level atomic.
/// </summary>
/// <remarks>
/// <para>
/// Each level has its own (see <see cref="AbstractLevel.TransferLock"/>), so levels ticking on threads of their own
/// never wait for each other's. Under it, for the entities the level owns (those whose <c>Level</c> is the level):
/// </para>
/// <list type="bullet">
/// <item>an item stack is merged, picked up or pulled into a hopper only while its entity isn't removed, and the
/// removal flag is set with the transfer, so a stack is never handed out twice;</item>
/// <item>an entity's region membership, its level and a save's or unload's snapshot of the region's entities change
/// together, so a save never sees an entity in two regions, or in none, mid-move;</item>
/// <item>riding links are checked and changed together, so riders and their vehicle share a level.</item>
/// </list>
/// <para>
/// An entity's <c>Level</c> changes only under the transfer lock of the level it names before the change, so code
/// that locks the level an entity names and finds it still names that level owns it until it lets go (see
/// <c>Entity.EnterOwningLevel</c>). Travel between levels is a handoff, never holding both levels' locks: the source
/// lets the entity go under its own lock, and the destination takes it in under its own (see <c>Entity.Travel</c>).
/// The destination publishes the entity at once, ticks it only once travel and any other level's tick of it are done
/// (see <c>Entity.TryClaimTick</c>), and the chunk it left stays pinned until it's settled (see
/// <c>AbstractLevel.Travel</c>).
/// </para>
/// <para>
/// A thread holds at most one level's transfer lock at a time; entering a second throws, as that is the way to a
/// deadlock between levels ticking in parallel. In the lock order it comes after a level's tick lock and container
/// click locks, and before a chunk's entity lock and an entity grid's lock. Never await, load chunks, fill a container
/// (its click lock comes first) or run plugin code holding it.
/// </para>
/// <para>
/// A level's tick, which holds its own tick lock, never waits for any level's simulation gate either: another level's
/// tick could be waiting for its own. Work that does, like a save run by a command, goes through
/// <see cref="AbstractLevel.RunOutsideTicksAsync"/>.
/// </para>
/// </remarks>
internal sealed class EntityTransferLock
{
    private readonly Lock gate = new();

    // The transfer lock this thread holds, and how many times it entered it. Nothing awaits under the lock, so the
    // thread that enters it is the one that leaves it.
    [ThreadStatic] private static EntityTransferLock? held;
    [ThreadStatic] private static int depth;

    /// <summary>
    /// Whether the current thread holds any level's transfer lock, under which loot mustn't be rolled (it may load
    /// chunks) and riding changes aren't deferred.
    /// </summary>
    internal static bool IsAnyHeldByCurrentThread => held is not null;

    /// <summary>Whether the current thread holds this lock.</summary>
    internal bool IsHeldByCurrentThread => this.gate.IsHeldByCurrentThread;

    /// <summary>Enters the lock, which the current thread may already hold, until the scope is disposed.</summary>
    /// <exception cref="InvalidOperationException">The current thread holds another level's transfer lock.</exception>
    internal Scope Enter()
    {
        if (held is not null && !ReferenceEquals(held, this))
            throw new InvalidOperationException("A thread may hold only one level's entity transfer lock at a time.");

        this.gate.Enter();
        held = this;
        depth++;
        return new Scope(this);
    }

    private void Exit()
    {
        if (--depth == 0)
            held = null;

        this.gate.Exit();
    }

    /// <summary>A hold of the lock, let go when disposed.</summary>
    internal readonly ref struct Scope
    {
        private readonly EntityTransferLock owner;

        internal Scope(EntityTransferLock owner) => this.owner = owner;

        public void Dispose() => this.owner.Exit();
    }
}
