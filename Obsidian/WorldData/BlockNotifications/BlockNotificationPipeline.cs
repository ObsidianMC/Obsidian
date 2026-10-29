using Obsidian.API.Blocks;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Features.Tree;

namespace Obsidian.WorldData.BlockNotifications;

/// <summary>
/// The live work a committed block change causes: vanilla's shape updates for the six neighbors, then their
/// <c>neighborChanged</c> reactions. Work is queued in FIFO order, coalesced while pending, and drained in a bounded loop, so
/// nothing recurses. Callers queue after committing a change; the drain runs now or at the level's next drain point.
/// </summary>
internal sealed class BlockNotificationPipeline
{
    /// <summary>
    /// Vanilla's chained neighbor update limit (<c>NeighborUpdater.MAX_CHAINED_NEIGHBOUR_UPDATES</c>). A drain processes at most this much
    /// work; the rest is dropped, so a runaway loop ends.
    /// </summary>
    public const int MaxWorkPerDrain = 10000;

    // Vanilla BlockBehaviour.UPDATE_SHAPE_ORDER, which neighbor notifications also use.
    private static readonly BlockFace[] order = [BlockFace.West, BlockFace.East, BlockFace.North, BlockFace.South, BlockFace.Down, BlockFace.Up];

    private readonly object sync = new();
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
    public void Enqueue(BlockChange change, BlockUpdateFlags flags = BlockUpdateFlags.All)
    {
        lock (this.sync)
        {
            // Vanilla invokes onPlace only for a block-type replacement, not for a property-only state change.
            // Preserve the old state so placement behaviors can make that distinction too.
            if (change.Old.RegistryId != change.New.RegistryId && (flags & BlockUpdateFlags.SkipOnPlace) == 0)
                this.Add(new NotificationWork(WorkKind.Placed, change.Position, change.Position, BlockFace.Down, change.Old));

            if ((flags & BlockUpdateFlags.KnownShape) == 0)
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
                await this.ProcessAsync(work);
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

    private async ValueTask ProcessAsync(NotificationWork work)
    {
        // Read at processing time, so each step sees the state committed before it.
        var state = this.world.GetLoadedBlock(work.Position);
        if (state is null)
            return;

        if (work.Kind == WorkKind.Placed)
        {
            if (work.OldState is IBlock old)
            {
                var change = new BlockChange(work.Position, old, state);
                if (this.behaviors.TryGetRemoved(old, out var removed))
                    await removed.OnRemovedAsync(change, this.world);

                if (this.behaviors.TryGetPlaced(state, out var placed))
                    await placed.OnPlacedAsync(change, this.world);
            }

            return;
        }

        var source = this.world.GetLoadedBlock(work.Source);
        if (work.Kind == WorkKind.Shape)
        {
            if (source is null)
                return;

            var rawBrightness = state.BlockClass() == "MushroomBlock" ? this.world.GetRawBrightness(work.Position) : 15;
            var updated = ShapeUpdater.UpdateShape(this.shapeView, state, work.Position, work.Face, source, rawBrightness);
            if (!updated.IsSameState(state))
            {
                if (updated.IsAir && !state.IsAir)
                {
                    // BaseRailBlock and RedstoneWireBlock use dropResources/removeBlock (no 2001 event); ordinary
                    // shape-destroyed blocks use Level.destroyBlock and do emit it.
                    var playEffect = state.BlockClass() != "RedstoneWireBlock" && !Rails.IsRail(state);
                    this.world.DropAndRemove(work.Position, state, playEffect);
                }
                else
                    await this.world.SetBlockAsync(work.Position, updated);
            }

            return;
        }

        if (this.behaviors.TryGet(state, out var behavior))
            await behavior.OnNeighborChangedAsync(new NeighborChange(work.Position, state, work.Source, source), this.world);
    }

    private enum WorkKind
    {
        Placed,
        Shape,
        Neighbor
    }

    // Face is the direction from Position toward Source, for shape reactions; neighbor reactions don't use it.
    private readonly record struct NotificationWork(WorkKind Kind, Vector Position, Vector Source, BlockFace Face, IBlock? OldState = null);
}
