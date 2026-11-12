using Obsidian.API.Blocks;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Features.Tree;

namespace Obsidian.WorldData.BlockNotifications;

/// <summary>
/// The live work a committed block change causes: vanilla's shape updates for the six neighbors, then their
/// <c>neighborChanged</c> reactions. Work is queued in FIFO order, coalesced while pending, and drained in a bounded loop, so
/// nothing recurses. Callers queue after committing a change; the drain runs now or at the level's next drain point.
/// </summary>
/// <remarks>
/// A live level queues its changes without shape steps (<see cref="Enqueue"/>'s <c>shapes</c>): its fluid level's
/// vanilla-ordered updater runs their shape updates (see <c>AbstractLevel.NotifyBlockChange</c>), so there the pipeline
/// runs only placement and neighbor reactions. Shape steps run where the pipeline is the only engine.
/// </remarks>
internal sealed class BlockNotificationPipeline
{
    /// <summary>
    /// Vanilla's chained neighbor update limit (<c>NeighborUpdater.MAX_CHAINED_NEIGHBOUR_UPDATES</c>). A drain processes at most this much
    /// work; the rest is dropped, so a runaway loop ends.
    /// </summary>
    public const int MaxWorkPerDrain = 10000;

    // Vanilla BlockBehaviour.UPDATE_SHAPE_ORDER, which neighbor notifications also use.
    private static readonly BlockFace[] order = [BlockFace.West, BlockFace.East, BlockFace.North, BlockFace.South, BlockFace.Down, BlockFace.Up];

    // The pipeline's own lock, not the tick lock: some callers queue and drain without the tick lock, such as
    // AbstractLevel's NotifyNeighborsAsync, which pressure plates and lever and button use reach.
    private readonly System.Threading.Lock sync = new();
    private readonly Queue<NotificationWork> queue = new();
    private readonly HashSet<NotificationWork> queued = [];
    private readonly IBlockChangeWorld world;
    private readonly NeighborBehaviors behaviors;
    private readonly LiveShapeView shapeView;
    private bool draining;
    private long dropped;

    public BlockNotificationPipeline(IBlockChangeWorld world, NeighborBehaviors behaviors)
    {
        this.world = world;
        this.behaviors = behaviors;
        this.shapeView = new LiveShapeView(world);
    }

    /// <summary>
    /// Work dropped because a drain hit <see cref="MaxWorkPerDrain"/>.
    /// </summary>
    public long DroppedWork
    {
        get { lock (this.sync) return this.dropped; }
    }

    /// <summary>
    /// Queues the notifications for a change that is already committed, as <paramref name="flags"/> ask: placement (unless
    /// <see cref="BlockUpdateFlags.SkipOnPlace"/>), shape updates (unless <see cref="BlockUpdateFlags.KnownShape"/>), and the six
    /// neighbor reactions (with <see cref="BlockUpdateFlags.Neighbors"/>). Doesn't drain and doesn't touch the world, so it's safe
    /// under the fluid lock.
    /// </summary>
    /// <param name="shapes">False when another engine runs the change's shape updates, as a live level's fluid level does.</param>
    public void Enqueue(BlockChange change, BlockUpdateFlags flags = BlockUpdateFlags.All, bool shapes = true)
    {
        lock (this.sync)
        {
            // Vanilla invokes onPlace only for a block-type replacement, not for a property-only state change.
            // Preserve the old state so placement behaviors can make that distinction too.
            if (change.Old.RegistryId != change.New.RegistryId && (flags & BlockUpdateFlags.SkipOnPlace) == 0)
                this.Add(new NotificationWork(WorkKind.Placed, change.Position, change.Position, BlockFace.Down, change.Old));

            if (shapes && (flags & BlockUpdateFlags.KnownShape) == 0)
            {
                foreach (var face in order)
                    this.Add(new NotificationWork(WorkKind.Shape, change.Position.Offset(face), change.Position, face.Opposite()));
            }

            if ((flags & BlockUpdateFlags.Neighbors) != 0)
            {
                foreach (var face in order)
                    this.Add(new NotificationWork(WorkKind.Neighbor, change.Position.Offset(face), change.Position, face.Opposite()));
            }
        }
    }

    /// <summary>
    /// Queues one neighbor reaction: <paramref name="target"/> hears that <paramref name="source"/> changed. Neighbor reactions
    /// read both blocks when they run, so the reaction sees what is committed then.
    /// </summary>
    public void EnqueueNeighbor(Vector target, Vector source)
    {
        lock (this.sync)
            this.Add(new NotificationWork(WorkKind.Neighbor, target, source, BlockFace.Down));
    }

    /// <summary>
    /// Queues the six neighbor reactions of <paramref name="position"/>, without a block change.
    /// </summary>
    public void EnqueueNeighbors(Vector position)
    {
        lock (this.sync)
        {
            foreach (var face in order)
                this.Add(new NotificationWork(WorkKind.Neighbor, position.Offset(face), position, face.Opposite()));
        }
    }

    /// <summary>
    /// Processes queued work in order until none is left or the per-drain limit is reached. A call made while a drain is running returns
    /// at once, and the running drain picks its work up.
    /// </summary>
    public async ValueTask DrainAsync()
    {
        lock (this.sync)
        {
            if (this.draining)
                return;

            this.draining = true;
        }

        var processed = 0;
        try
        {
            while (true)
            {
                NotificationWork work;
                lock (this.sync)
                {
                    if (this.queue.Count == 0)
                    {
                        this.draining = false;
                        return;
                    }

                    if (processed == MaxWorkPerDrain)
                    {
                        this.dropped += this.queue.Count;
                        this.queue.Clear();
                        this.queued.Clear();
                        this.draining = false;
                        return;
                    }

                    work = this.queue.Dequeue();
                    this.queued.Remove(work);
                }

                processed++;

                // Most steps finish synchronously (no reaction, or one that doesn't yield): finish those inline and
                // await only a step that is still running.
                var step = this.Process(work);
                if (step.IsCompletedSuccessfully)
                    step.GetAwaiter().GetResult();
                else
                    await step;
            }
        }
        catch
        {
            lock (this.sync)
                this.draining = false;

            throw;
        }
    }

    // Caller holds sync. A pending duplicate is coalesced; a processed one can queue again.
    private void Add(NotificationWork work)
    {
        if (this.queued.Add(work))
            this.queue.Enqueue(work);
    }

    // Not an async method: a step that completes synchronously returns a completed ValueTask without building a state
    // machine. Only placement, which may await two behaviours in turn, gets one.
    private ValueTask Process(NotificationWork work)
    {
        // Read at processing time, so each step sees the state committed before it.
        var state = this.world.GetLoadedBlock(work.Position);
        if (state is null)
            return ValueTask.CompletedTask;

        return work.Kind switch
        {
            WorkKind.Placed => this.ProcessPlacedAsync(work, state),
            WorkKind.Shape => this.ProcessShape(work, state),
            _ => this.ProcessNeighbor(work, state)
        };
    }

    private async ValueTask ProcessPlacedAsync(NotificationWork work, IBlock state)
    {
        if (work.OldState is not IBlock old)
            return;

        var change = new BlockChange(work.Position, old, state);
        if (this.behaviors.TryGetRemoved(old, out var removed))
            await removed.OnRemovedAsync(change, this.world);

        if (this.behaviors.TryGetPlaced(state, out var placed))
            await placed.OnPlacedAsync(change, this.world);
    }

    private ValueTask ProcessShape(NotificationWork work, IBlock state)
    {
        var source = this.world.GetLoadedBlock(work.Source);
        if (source is null)
            return ValueTask.CompletedTask;

        ShapeUpdater.ScheduleFluidTick(this.shapeView, state, work.Position, source);
        var updated = this.world.UpdateShape(work.Position, state, work.Face, work.Source, source)
            ?? ShapeUpdater.UpdateShape(this.shapeView, state, work.Position, work.Face, source,
                scheduleFluidTick: false);
        if (updated.IsSameState(state))
            return ValueTask.CompletedTask;

        if (updated.IsAir && !state.IsAir)
        {
            // BaseRailBlock and RedstoneWireBlock use dropResources/removeBlock (no 2001 event); ordinary
            // shape-destroyed blocks use Level.destroyBlock and do emit it.
            var playEffect = state.BlockClass() != "RedstoneWireBlock" && !Rails.IsRail(state);
            this.world.DropAndRemove(work.Position, state, playEffect);
            return ValueTask.CompletedTask;
        }

        return this.world.SetBlockAsync(work.Position, updated);
    }

    private ValueTask ProcessNeighbor(NotificationWork work, IBlock state)
    {
        // Most neighbours have no reaction: check that first, so their step reads nothing more. The check happens
        // here rather than when the step is queued, because queueing must not read the world (it runs under the
        // fluid lock) and the reaction belongs to whatever block is committed when the step runs.
        if (!this.behaviors.TryGet(state, out var behavior))
            return ValueTask.CompletedTask;

        var source = this.world.GetLoadedBlock(work.Source);
        var change = new NeighborChange(work.Position, state, work.Source, source);
        return behavior.OnNeighborChangedAsync(change, this.world);
    }

    private enum WorkKind
    {
        Placed,
        Shape,
        Neighbor
    }

    /// <summary>
    /// One queued step, and its coalescing key: two steps are duplicates when every field matches, the old state by
    /// its own equality. Face is the direction from Position toward Source, for shape reactions; neighbor reactions
    /// don't use it, but it still tells their keys apart.
    /// </summary>
    /// <remarks>
    /// Hand-written rather than a record struct, whose hash runs every field through its default comparer: this one
    /// hashes the coordinates alone, which is enough to spread the work queued around a change, and leaves the
    /// other fields to <see cref="Equals(NotificationWork)"/>.
    /// </remarks>
    private readonly struct NotificationWork(
        WorkKind kind, Vector position, Vector source, BlockFace face, IBlock? oldState = null)
        : IEquatable<NotificationWork>
    {
        public WorkKind Kind { get; } = kind;

        public Vector Position { get; } = position;

        public Vector Source { get; } = source;

        public BlockFace Face { get; } = face;

        public IBlock? OldState { get; } = oldState;

        public bool Equals(NotificationWork other)
        {
            if (this.Kind != other.Kind || this.Face != other.Face)
                return false;

            if (!this.Position.Equals(other.Position) || !this.Source.Equals(other.Source))
                return false;

            return EqualityComparer<IBlock?>.Default.Equals(this.OldState, other.OldState);
        }

        public override bool Equals(object? obj) => obj is NotificationWork other && this.Equals(other);

        public override int GetHashCode()
        {
            var position = this.Position;
            var source = this.Source;
            var hash = position.X * 73856093;
            hash ^= position.Y * 19349663;
            hash ^= position.Z * 83492791;
            hash = hash * 31 + source.X;
            hash = hash * 31 + source.Y;
            hash = hash * 31 + source.Z;
            return hash;
        }
    }
}
